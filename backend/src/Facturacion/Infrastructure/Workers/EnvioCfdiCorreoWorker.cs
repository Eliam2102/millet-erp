using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Envios;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (D17) que drena <see cref="BitacoraEnvioCorreo"/> en estado
/// <see cref="EstadoEnvioCorreo.Pendiente"/> o <see cref="EstadoEnvioCorreo.Fallido"/>
/// (con reintentos &lt; máximo): genera el PDF de la factura, obtiene el XML del
/// repo de CFDI y entrega vía <see cref="INotificacionService"/> (§12.5 levantamiento).
/// </summary>
public sealed class EnvioCfdiCorreoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EnvioCfdiCorreoOptions> _options;
    private readonly ILogger<EnvioCfdiCorreoWorker> _logger;

    public EnvioCfdiCorreoWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EnvioCfdiCorreoOptions> options,
        ILogger<EnvioCfdiCorreoWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Disabled)
        {
            _logger.LogInformation("[EnvioCfdiCorreoWorker] Disabled — loop no inicia.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EnvioCfdiCorreoWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests.</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var pdfGen = sp.GetRequiredService<IGenerarPdfFacturaPort>();
        var cfdiRepo = sp.GetRequiredService<ICfdiRepositorioPort>();
        var notif = sp.GetRequiredService<INotificacionService>();
        var clock = sp.GetRequiredService<IClock>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(EnvioCfdiCorreoWorker));
        using var bypass = empresaContext.Bypass();

        var pendientes = await db.BitacorasEnvioCorreo
            .Where(b => b.Estado == EstadoEnvioCorreo.Pendiente
                        || (b.Estado == EstadoEnvioCorreo.Fallido && b.Intentos < opts.MaxIntentos))
            .OrderBy(b => b.CreatedAt)
            .Take(opts.BatchSize)
            .ToListAsync(cancellationToken);

        var enviadas = 0;
        foreach (var bitacora in pendientes)
        {
            var factura = await db.FacturasVenta
                .Include(f => f.Lineas)
                .FirstOrDefaultAsync(f => f.Id == bitacora.ComprobanteId, cancellationToken);

            if (factura is null)
            {
                bitacora.MarcarFallido("La factura asociada no existe.");
                continue;
            }

            try
            {
                var pdf = pdfGen.Generar(factura, FormatoPdfFactura.Bilingue);
                var xml = factura.Uuid is null
                    ? null
                    : (await cfdiRepo.ObtenerPorUuidAsync(factura.Uuid, cancellationToken))?.XmlContenido;

                var aceptado = await notif.EnviarCfdiPorCorreoAsync(
                    new EnvioCfdiCorreo(
                        bitacora.EmpresaId, factura.Id, bitacora.Destinatario,
                        factura.Folio, factura.Uuid, pdf.Contenido, xml),
                    cancellationToken);

                if (aceptado)
                {
                    bitacora.MarcarEnviado(clock.UtcNow);
                    factura.MarcarEnviadoCorreo();
                    enviadas++;
                }
                else
                {
                    bitacora.MarcarFallido("El servicio de notificación rechazó el envío.");
                }
            }
            catch (Exception ex)
            {
                bitacora.MarcarFallido(ex.Message);
                _logger.LogWarning(ex, "[EnvioCfdiCorreoWorker] Envío falló para comprobante {Comprobante}", bitacora.ComprobanteId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (pendientes.Count > 0)
        {
            _logger.LogInformation(
                "[EnvioCfdiCorreoWorker] Tick: {Total} pendientes, {Enviadas} enviadas.",
                pendientes.Count, enviadas);
        }

        return enviadas;
    }
}

public sealed class EnvioCfdiCorreoOptions
{
    public const string SectionName = "Facturacion:Workers:EnvioCfdiCorreo";

    public bool Disabled { get; init; }
    public int IntervalSeconds { get; init; } = 60;
    public int MaxIntentos { get; init; } = 3;
    public int BatchSize { get; init; } = 50;
}
