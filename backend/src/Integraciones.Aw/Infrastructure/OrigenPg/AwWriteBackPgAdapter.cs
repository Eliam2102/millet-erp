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
        // TR-01 (B6): el UPDATE por pedido (uuid/estado en TODAS las versiones) se retiró; la factura
        // viaja ahora como fila Documento de aw_retorno (AwRetornoAdapter). Solo queda el acuse por solicitud.
        if (writeBack.SolicitudId == Guid.Empty)
        {
            _logger.LogWarning("[AwWriteBackPgAdapter] write-back por pedido ignorado (retirado en TR-01). pedido={Pedido}", writeBack.NumeroPedido);
            return;
        }

        // COALESCE preserva claim/uuid previos: nunca se degradan a NULL (D18).
        // procesada_at llega como parámetro (no SYSDATETIMEOFFSET()) para que el
        // SQL corra igual en el origen de demo PostgreSQL.
        const string sql = """
              UPDATE dbo.aw_solicitud_pedido
                 SET erp_pedido_id      = COALESCE(@erpPedidoId, erp_pedido_id),
                     estado_facturacion = COALESCE(@estado, estado_facturacion),
                     uuid               = COALESCE(@uuid, uuid),
                     resultado          = @resultado,
                     motivo             = @motivo,
                     procesada_at       = @procesadaAt
               WHERE solicitud_id = @solicitudId
              """;

        var afectadas = await PgPlumbing.EjecutarAsync(_connectionFactory, _options, _logger, "WriteBack",
            async connection =>
            {
                using var command = PgPlumbing.CrearCommand(connection, sql, _options);
                AgregarParametros(command, writeBack);
                return await command.ExecuteNonQueryAsync(cancellationToken);
            }, cancellationToken);

        if (afectadas == 0)
        {
            // No es error del canal: la fila puede no existir aún (p.ej.
            // write-back de timbrado de un pedido pre-integración). Se
            // loggea para auditoría; el worker NO debe reintentar infinito
            // por esto — cuenta como éxito de canal.
            _logger.LogWarning(
                "[AwWriteBackPgAdapter] write-back sin filas afectadas. solicitud={SolicitudId} pedido={Pedido}",
                writeBack.SolicitudId, writeBack.NumeroPedido);
            return;
        }

        _logger.LogInformation(
            "[AwWriteBackPgAdapter] write-back OK. pedido={Pedido} resultado={Resultado} estado={Estado} uuid={Uuid} filas={Filas}",
            writeBack.NumeroPedido,
            writeBack.Resultado, writeBack.EstadoFacturacion, writeBack.Uuid, afectadas);
    }

    private static void AgregarParametros(DbCommand command, AwWriteBack wb)
    {
        PgPlumbing.Param(command, "@erpPedidoId", DbType.Guid, wb.ErpPedidoId);
        PgPlumbing.Param(command, "@estado", DbType.String, wb.EstadoFacturacion);
        PgPlumbing.Param(command, "@uuid", DbType.String, wb.Uuid);

        PgPlumbing.Param(command, "@resultado", DbType.Int16, (short)wb.Resultado);
        PgPlumbing.Param(command, "@motivo", DbType.String, wb.Motivo);
        PgPlumbing.Param(command, "@procesadaAt", DbType.DateTimeOffset, DateTimeOffset.UtcNow);
        PgPlumbing.Param(command, "@solicitudId", DbType.Guid, wb.SolicitudId);
    }
}
