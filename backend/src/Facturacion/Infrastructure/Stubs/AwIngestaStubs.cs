using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// Fallback DEV del reader de la cola A+W (deuda &lt;AwVistaPedidos&gt; CERRADA por
/// ADR-0048 PR3: el adapter real es <c>AwSolicitudesSqlReader</c> en
/// Integraciones.Aw/Pedidos, activo vía toggle AwIntegracionDb). En dev
/// devuelve vacío (no hay Hybrid Connection a SER-DATA). El reader real lo
/// implementa <c>Integraciones.Aw</c> sobre la tabla-puente on-prem.
/// </summary>
public sealed class StubAwSolicitudesReader : IAwSolicitudesReader
{
    private readonly ILogger<StubAwSolicitudesReader> _logger;

    public StubAwSolicitudesReader(ILogger<StubAwSolicitudesReader> logger) => _logger = logger;

    public Task<IReadOnlyList<SolicitudAw>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[StubAwSolicitudesReader] LeerPendientes → 0 (stub F3-PR1b, sin Hybrid Connection)");
        return Task.FromResult<IReadOnlyList<SolicitudAw>>([]);
    }

    public Task<LecturaPedidoAw> LeerDatosPedidoAsync(string numeroPedido, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[StubAwSolicitudesReader] LeerDatosPedido({Pedido}) → sin datos (stub F3-PR1b)", numeroPedido);
        return Task.FromResult(LecturaPedidoAw.DatosInvalidos(
            Domain.Ingesta.MotivoExcepcion.Otro, "stub dev — sin acceso a las vistas A+W"));
    }
}

/// <summary>
/// Fallback DEV del write-back a A+W (mitad A+W de &lt;WriteBackOrigenes&gt; CERRADA
/// por ADR-0048 PR3/PR5: <c>AwWriteBackSqlAdapter</c> + WriteBackResultadoWorker;
/// la pata Planta Pintura sigue en &lt;PlantaPinturaOrigenes&gt;). Loggea el
/// claim/resultado sin escribir en la tabla-puente. <c>ingesta_control</c> sigue
/// siendo la fuente de verdad. El canal real lo implementa <c>Integraciones.Aw</c>.
/// </summary>
public sealed class StubAwWriteBackPort : IAwWriteBackPort
{
    private readonly ILogger<StubAwWriteBackPort> _logger;

    public StubAwWriteBackPort(ILogger<StubAwWriteBackPort> logger) => _logger = logger;

    public Task EscribirResultadoAsync(AwWriteBack writeBack, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[StubAwWriteBackPort] WRITE-BACK FAKE pedido={Pedido} claim={Claim} estado={Estado} resultado={Resultado} motivo={Motivo} " +
            "(stub fallback dev — adapter real vía toggle AwIntegracionDb, ADR-0048)",
            writeBack.NumeroPedido, writeBack.ErpPedidoId, writeBack.EstadoFacturacion, writeBack.Resultado, writeBack.Motivo);
        return Task.CompletedTask;
    }
}
