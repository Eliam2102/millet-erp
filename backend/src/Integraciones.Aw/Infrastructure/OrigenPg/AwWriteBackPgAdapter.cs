using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.OrigenPg;

/// <summary>Write-back PostgreSQL de la copia de demo. Conserva claim y UUID mediante COALESCE; no accede a SQL Server.</summary>
public sealed class AwWriteBackPgAdapter : IAwWriteBackPort
{
    private readonly IIntegracionSqlConnectionFactory _connectionFactory;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwWriteBackPgAdapter> _logger;

    public AwWriteBackPgAdapter(
        IIntegracionSqlConnectionFactory connectionFactory,
        IOptions<AwPedidosOptions> options,
        ILogger<AwWriteBackPgAdapter> logger)
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

        var afectadas = await PgPlumbing.EjecutarAsync(_connectionFactory, _options, _logger, "WriteBack",
            async connection =>
            {
                using var command = PgPlumbing.CrearCommand(connection, sql, _options);
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
                "[AwWriteBackPgAdapter] write-back sin filas afectadas. porSolicitud={PorSolicitud} solicitud={SolicitudId} pedido={Pedido}",
                porSolicitud, writeBack.SolicitudId, writeBack.NumeroPedido);
            return;
        }

        _logger.LogInformation(
            "[AwWriteBackPgAdapter] write-back OK. modo={Modo} pedido={Pedido} resultado={Resultado} estado={Estado} uuid={Uuid} filas={Filas}",
            porSolicitud ? "solicitud" : "pedido", writeBack.NumeroPedido,
            writeBack.Resultado, writeBack.EstadoFacturacion, writeBack.Uuid, afectadas);
    }

    private static void AgregarParametros(DbCommand command, AwWriteBack wb, bool porSolicitud)
    {
        PgPlumbing.Param(command, "@erpPedidoId", DbType.Guid, wb.ErpPedidoId);
        PgPlumbing.Param(command, "@estado", DbType.String, wb.EstadoFacturacion);
        PgPlumbing.Param(command, "@uuid", DbType.String, wb.Uuid);

        if (porSolicitud)
        {
            PgPlumbing.Param(command, "@resultado", DbType.Int16, (short)wb.Resultado);
            PgPlumbing.Param(command, "@motivo", DbType.String, wb.Motivo);
            PgPlumbing.Param(command, "@procesadaAt", DbType.DateTimeOffset, DateTimeOffset.UtcNow);
            PgPlumbing.Param(command, "@solicitudId", DbType.Guid, wb.SolicitudId);
        }
        else
        {
            PgPlumbing.Param(command, "@numeroPedido", DbType.String, wb.NumeroPedido);
        }
    }
}
