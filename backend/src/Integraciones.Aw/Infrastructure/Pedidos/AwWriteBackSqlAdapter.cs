using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
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
/// <para>Solo escribe el resultado del procesamiento por solicitud (claim + resultado +
/// motivo + procesada_at). TR-01: el modo por pedido (uuid/estado en todas las filas, B6)
/// se retiró; la factura la escribe <c>AwRetornoAdapter</c> en <c>aw_retorno</c>.</para>
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
        // TR-01 (B6): el UPDATE por pedido (uuid/estado en TODAS las versiones) se retiró; la factura
        // viaja ahora como fila Documento de aw_retorno (AwRetornoAdapter). Solo queda el acuse por solicitud.
        if (writeBack.SolicitudId == Guid.Empty)
        {
            _logger.LogWarning("[AwWriteBackSqlAdapter] write-back por pedido ignorado (retirado en TR-01). pedido={Pedido}", writeBack.NumeroPedido);
            return;
        }

        // COALESCE preserva claim/uuid previos: nunca se degradan a NULL (D18).
        const string sql = """
              UPDATE dbo.aw_solicitud_pedido
                 SET erp_pedido_id      = COALESCE(@erpPedidoId, erp_pedido_id),
                     estado_facturacion = COALESCE(@estado, estado_facturacion),
                     [uuid]             = COALESCE(@uuid, [uuid]),
                     resultado          = @resultado,
                     motivo             = @motivo,
                     procesada_at       = SYSDATETIMEOFFSET()
               WHERE solicitud_id = @solicitudId
              """;

        try
        {
            using var connection = _connectionFactory.CreateConnection();

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(_options.SqlConnectTimeoutSeconds));
            try
            {
                await connection.OpenAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (
                connectCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new AwReaderException(
                    $"SQL connect timeout tras {_options.SqlConnectTimeoutSeconds}s (write-back).",
                    kind: "connect_timeout", isTransient: true);
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = _options.SqlQueryTimeoutSeconds;
            AgregarParametros(command, writeBack);

            var afectadas = await command.ExecuteNonQueryAsync(cancellationToken);
            if (afectadas == 0)
            {
                // No es error del canal: la fila puede no existir aún (p.ej.
                // write-back de timbrado de un pedido pre-integración). Se
                // loggea para auditoría; el worker NO debe reintentar infinito
                // por esto — cuenta como éxito de canal.
                _logger.LogWarning(
                    "[AwWriteBackSqlAdapter] write-back sin filas afectadas. solicitud={SolicitudId} pedido={Pedido}",
                    writeBack.SolicitudId, writeBack.NumeroPedido);
                return;
            }

            _logger.LogInformation(
                "[AwWriteBackSqlAdapter] write-back OK. pedido={Pedido} resultado={Resultado} estado={Estado} uuid={Uuid} filas={Filas}",
                writeBack.NumeroPedido,
                writeBack.Resultado, writeBack.EstadoFacturacion, writeBack.Uuid, afectadas);
        }
        catch (SqlException ex) when (ex.Number == 18456)
        {
            _logger.LogError(ex, "[AwWriteBackSqlAdapter] auth failure #{Number}", ex.Number);
            throw new AwReaderException(
                $"SQL Server auth failed (#{ex.Number}): {ex.Message}",
                kind: "auth", isTransient: false, inner: ex);
        }
        catch (SqlException ex) when (ex.Number is -2 or 11 or 121)
        {
            throw new AwReaderException(
                $"SQL write-back timeout ({_options.SqlQueryTimeoutSeconds}s).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (SqlException ex)
        {
            throw new AwReaderException(
                $"SQL Server error en write-back (#{ex.Number}): {ex.Message}",
                kind: "connection", isTransient: true, inner: ex);
        }
    }

    private static void AgregarParametros(DbCommand command, AwWriteBack wb)
    {
        command.Parameters.Add(new SqlParameter("@erpPedidoId", SqlDbType.UniqueIdentifier)
        { Value = (object?)wb.ErpPedidoId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@estado", SqlDbType.NVarChar, 20)
        { Value = (object?)wb.EstadoFacturacion ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@uuid", SqlDbType.NVarChar, 36)
        { Value = (object?)wb.Uuid ?? DBNull.Value });

        command.Parameters.Add(new SqlParameter("@resultado", SqlDbType.TinyInt)
        { Value = (byte)wb.Resultado });
        command.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 500)
        { Value = (object?)wb.Motivo ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@solicitudId", SqlDbType.UniqueIdentifier)
        { Value = wb.SolicitudId });
    }
}
