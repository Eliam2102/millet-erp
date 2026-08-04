using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (§9 diseño) que resuelve las <see cref="SolicitudCancelacion"/>
/// en <see cref="EstadoSolicitudCancelacion.EnProceso"/> consultando el estatus
/// del CFDI ante el SAT (<see cref="ICfdiTimbradoPort.ConsultarEstatusAsync"/>).
/// Cuando el SAT reporta el CFDI como no vigente, cancela el comprobante y aplica
/// los efectos (la factura final libera su pedido; el anticipo pasa a Cancelado).
/// El stub reporta vigente, así que en dev el poller no actúa — el camino síncrono
/// (PAC acepta de inmediato) resuelve la mayoría hasta F12.
/// </summary>
public sealed class CancelacionSatPollerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<CancelacionSatPollerOptions> _options;
    private readonly ILogger<CancelacionSatPollerWorker> _logger;

    public CancelacionSatPollerWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<CancelacionSatPollerOptions> options,
        ILogger<CancelacionSatPollerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Disabled)
        {
            _logger.LogInformation("[CancelacionSatPollerWorker] Disabled — loop no inicia.");
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
                _logger.LogError(ex, "[CancelacionSatPollerWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CurrentValue.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>Internal para tests. Devuelve cuántas solicitudes resolvió (Aceptada).</summary>
    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var fiscal = sp.GetRequiredService<ICfdiTimbradoPort>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        var eventos = sp.GetRequiredService<IIntegrationEventPublisher>();
        var clock = sp.GetRequiredService<IClock>();

        using var bypass = empresaContext.Bypass();

        var pendientes = await db.SolicitudesCancelacion
            .Where(s => s.Estado == EstadoSolicitudCancelacion.EnProceso)
            .OrderBy(s => s.SolicitadaEn)
            .Take(opts.BatchSize)
            .ToListAsync(cancellationToken);

        var resueltas = 0;
        foreach (var solicitud in pendientes)
        {
            var comprobante = await db.Comprobantes
                .FirstOrDefaultAsync(c => c.Id == solicitud.ComprobanteId, cancellationToken);
            if (comprobante is null || string.IsNullOrWhiteSpace(comprobante.Uuid))
                continue;

            var sello8 = comprobante.SelloCfdi is { Length: >= 8 } sello ? sello[^8..] : null;
            var estatus = await fiscal.ConsultarEstatusAsync(
                new EstatusCfdiSolicitud(
                    comprobante.EmpresaId, comprobante.Uuid!, comprobante.RfcEmisor,
                    comprobante.ReceptorRfc, comprobante.Total, sello8),
                cancellationToken);
            if (estatus.EsVigente)
                continue; // el SAT aún no procesa la cancelación

            if (comprobante.Estado == EstadoTimbrado.CancelacionPendiente)
                comprobante.MarcarCancelado();
            solicitud.MarcarAceptada(estatus.EstatusSat, clock.UtcNow);
            await AplicarEfectosCancelacionAsync(db, comprobante, clock.UtcNow, cancellationToken);
            // F10-PR1: evento de comprobante cancelado al resolverse el poller.
            await eventos.PublishAsync(new ComprobanteCanceladoIntegrationEvent(
                comprobante.EmpresaId, clock.UtcNow, comprobante.Id, comprobante.Tipo.ToString(), comprobante.Uuid ?? string.Empty),
                cancellationToken);
            resueltas++;
        }

        if (resueltas > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[CancelacionSatPollerWorker] Tick: {Resueltas}/{Total} cancelaciones resueltas.", resueltas, pendientes.Count);
        }

        return resueltas;
    }

    private static async Task AplicarEfectosCancelacionAsync(
        FacturacionDbContext db, Comprobante comprobante, DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        switch (comprobante)
        {
            case FacturaVenta { PedidoFacturableId: { } pedidoId }:
                var pedido = await db.PedidosFacturables.FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken);
                pedido?.RevertirAFacturable();

                // ADR-0048 D3 (PR5): espejo del hook de SolicitarCancelacionHandler.
                if (pedido?.Origen == Domain.Pedidos.OrigenPedido.Aw)
                {
                    var control = await db.IngestaControles
                        .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, cancellationToken);
                    if (control is not null)
                    {
                        control.SolicitarWriteBackEstado("SinFacturar", uuid: null, ahora);
                        control.CambiarEstado(Domain.Ingesta.EstadoIngesta.Importado);
                    }
                }
                break;

            case FacturaAnticipo:
                var anticipo = await db.Anticipos.FirstOrDefaultAsync(a => a.FacturaAnticipoId == comprobante.Id, cancellationToken);
                anticipo?.Cancelar();
                break;
        }
    }
}

public sealed class CancelacionSatPollerOptions
{
    public const string SectionName = "Facturacion:Workers:CancelacionSatPoller";

    public bool Disabled { get; init; }
    public int IntervalSeconds { get; init; } = 300;
    public int BatchSize { get; init; } = 50;
}
