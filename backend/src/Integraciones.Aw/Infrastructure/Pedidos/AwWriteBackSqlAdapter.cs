using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Adapter REAL de <see cref="IAwWriteBackPort"/> (flujo 2, ADR-0048 D3;
/// cierra la mitad A+W de PLATFORM-TODO(&lt;WriteBackOrigenes&gt;)). Escribe
/// SOLO las columnas write-back de <c>dbo.aw_solicitud_pedido</c>
/// (doc 04 §5) — el login <c>millet_erp_integracion</c> tiene UPDATE
/// restringido por columna, gemelo del contrato en permisos.
///
/// <para>Dos modos según <see cref="AwWriteBack.SolicitudId"/>:</para>
/// <list type="bullet">
///   <item><b>Por solicitud</b> (<c>SolicitudId != Guid.Empty</c>): resultado
///   del procesamiento — claim + resultado + motivo + procesada_at.</item>
///   <item><b>Por pedido</b> (<c>Guid.Empty</c>, timbrado/cancelación):
///   uuid + estado_facturacion en TODAS las filas del pedido. Gap
///   G-writeback (spec §7): si el equipo A+W prefiere solo la última
///   <c>version</c>, se ajusta el WHERE aquí — un solo lugar.</item>
/// </list>
///
/// <para>Idempotente por construcción: UPDATE absoluto de valores finales;
/// un reintento re-escribe lo mismo. El claim nunca se borra (D18) — este
/// adapter jamás escribe NULL sobre <c>erp_pedido_id</c>.</para>
/// </summary>
public sealed class AwWriteBackSqlAdapter : IAwWriteBackPort
{
    private readonly IIntegracionSqlConnectionFactory _connectionFactory;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwWriteBackSqlAdapter> _logger;

    public AwWriteBackSqlAdapter(
        IIntegracionSqlConnectionFactory connectionFactory,
        IOptions<AwPedidosOptions> options,
        ILogger<AwWriteBackSqlAdapter> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EscribirResultadoAsync(AwWriteBack writeBack, CancellationToken cancellationToken)
    {
        var porSolicitud = writeBack.SolicitudId != Guid.Empty;

        // COALESCE preserva claim/uuid previos: nunca se degradan a NULL (D18).
        // procesada_at llega como parámetro (no SYSDATETIMEOFFSET()) para que el
        // SQL corra igual en el origen de demo PostgreSQL.
        var sql = porSolicitud
            ? """
              UPDATE dbo.aw_solicitud_pedido
                 SET erp_pedido_id      = COALESCE(@erpPedidoId, erp_pedido_id),
                     estado_facturacion = COALESCE(@estado, estado_facturacion),
                     uuid               = COALESCE(@uuid, uuid),
                     resultado          = @resultado,
                     motivo             = @motivo,
                     procesada_at       = @procesadaAt
               WHERE solicitud_id = @solicitudId
              """
            : """
              UPDATE dbo.aw_solicitud_pedido
                 SET erp_pedido_id      = COALESCE(@erpPedidoId, erp_pedido_id),
                     estado_facturacion = COALESCE(@estado, estado_facturacion),
                     uuid               = COALESCE(@uuid, uuid)
               WHERE numero_pedido = @numeroPedido
              """;

        var afectadas = await SqlPlumbing.EjecutarAsync(_connectionFactory, _options, _logger, "WriteBack",
            async connection =>
            {
                using var command = SqlPlumbing.CrearCommand(connection, sql, _options);
                AgregarParametros(command, writeBack, porSolicitud);
                return await command.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);

        if (afectadas == 0)
        {
            // No es error del canal: la fila puede no existir aún (p.ej.
            // write-back de timbrado de un pedido pre-integración). Se
            // loggea para auditoría; el worker NO debe reintentar infinito
            // por esto — cuenta como éxito de canal.
            _logger.LogWarning(
                "[AwWriteBackSqlAdapter] write-back sin filas afectadas. porSolicitud={PorSolicitud} solicitud={SolicitudId} pedido={Pedido}",
                porSolicitud, writeBack.SolicitudId, writeBack.NumeroPedido);
            return;
        }

        _logger.LogInformation(
            "[AwWriteBackSqlAdapter] write-back OK. modo={Modo} pedido={Pedido} resultado={Resultado} estado={Estado} uuid={Uuid} filas={Filas}",
            porSolicitud ? "solicitud" : "pedido", writeBack.NumeroPedido,
            writeBack.Resultado, writeBack.EstadoFacturacion, writeBack.Uuid, afectadas);
    }

    private static void AgregarParametros(DbCommand command, AwWriteBack wb, bool porSolicitud)
    {
        SqlPlumbing.Param(command, "@erpPedidoId", DbType.Guid, wb.ErpPedidoId);
        SqlPlumbing.Param(command, "@estado", DbType.String, wb.EstadoFacturacion);
        SqlPlumbing.Param(command, "@uuid", DbType.String, wb.Uuid);

        if (porSolicitud)
        {
            SqlPlumbing.Param(command, "@resultado", DbType.Byte, (byte)wb.Resultado);
            SqlPlumbing.Param(command, "@motivo", DbType.String, wb.Motivo);
            SqlPlumbing.Param(command, "@procesadaAt", DbType.DateTimeOffset, DateTimeOffset.UtcNow);
            SqlPlumbing.Param(command, "@solicitudId", DbType.Guid, wb.SolicitudId);
        }
        else
        {
            SqlPlumbing.Param(command, "@numeroPedido", DbType.String, wb.NumeroPedido);
        }
    }
}
