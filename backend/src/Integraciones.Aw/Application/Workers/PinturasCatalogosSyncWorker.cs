using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Worker periódico en background que extrae catálogos del ERP (clientes, servicios, obras, empleados)
/// y los publica de forma idempotente en la base de datos de integración (<c>MILLET_INTEGRACION</c>)
/// para consumo del Portal de Pinturas.
/// </summary>
public sealed class PinturasCatalogosSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PinturasCatalogosOptions> _options;
    private readonly ILogger<PinturasCatalogosSyncWorker> _logger;

    public PinturasCatalogosSyncWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PinturasCatalogosOptions> options,
        ILogger<PinturasCatalogosSyncWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[PinturasCatalogosSyncWorker] Iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SincronizarUnaVezAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PinturasCatalogosSyncWorker] Error no controlado en ciclo de sincronización de catálogos.");
            }

            var delay = TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.IntervaloMinutos));
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("[PinturasCatalogosSyncWorker] Detenido.");
    }

    internal async Task<PinturasPublicacionResultado> SincronizarUnaVezAsync(CancellationToken ct)
    {
        if (!_options.CurrentValue.Habilitado)
        {
            _logger.LogDebug("[PinturasCatalogosSyncWorker] Sincronización omitida: worker deshabilitado en configuración.");
            return PinturasPublicacionResultado.Vacio();
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var originContext = sp.GetRequiredService<IAuditOriginContext>();
            using var origin = originContext.SetOrigin(nameof(PinturasCatalogosSyncWorker));

            var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            var extractor = sp.GetRequiredService<IPinturasCatalogosExtractor>();
            var publisher = sp.GetRequiredService<IPinturasCatalogosPublisher>();

            var batch = await extractor.ExtraerAsync(ct);
            var resultado = await publisher.PublicarAsync(batch, ct);

            _logger.LogInformation(
                "[PinturasCatalogosSyncWorker] Sincronización de catálogos completada con éxito. Clientes={Clientes}, Servicios={Servicios}, Obras={Obras}, Empleados={Empleados}, Total={Total}",
                resultado.ClientesPublicados,
                resultado.ServiciosPublicados,
                resultado.ObrasPublicadas,
                resultado.EmpleadosPublicados,
                resultado.TotalPublicados);

            return resultado;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PinturasCatalogosSyncWorker] Error durante la sincronización de catálogos de Pinturas.");
            return PinturasPublicacionResultado.Vacio();
        }
    }
}
