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
        var sql = porSolicitud
            ? """
              UPDATE dbo.aw_solicitud_pedido
                 SET erp_pedido_id      = COALESCE(@erpPedidoId, erp_pedido_id),
                     estado_facturacion = COALESCE(@estado, estado_facturacion),
                     [uuid]             = COALESCE(@uuid, [uuid]),
                     resultado          = @resultado,
                     motivo             = @motivo,
                     procesada_at       = SYSDATETIMEOFFSET()
               WHERE solicitud_id = @solicitudId
              """
            : """
              UPDATE dbo.aw_solicitud_pedido
                 SET erp_pedido_id      = COALESCE(@erpPedidoId, erp_pedido_id),
                     estado_facturacion = COALESCE(@estado, estado_facturacion),
                     [uuid]             = COALESCE(@uuid, [uuid])
               WHERE numero_pedido = @numeroPedido
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
            AgregarParametros(command, writeBack, porSolicitud);

            var afectadas = await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static void AgregarParametros(DbCommand command, AwWriteBack wb, bool porSolicitud)
    {
        command.Parameters.Add(new SqlParameter("@erpPedidoId", SqlDbType.UniqueIdentifier)
        { Value = (object?)wb.ErpPedidoId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@estado", SqlDbType.NVarChar, 20)
        { Value = (object?)wb.EstadoFacturacion ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@uuid", SqlDbType.NVarChar, 36)
        { Value = (object?)wb.Uuid ?? DBNull.Value });

        if (porSolicitud)
        {
            command.Parameters.Add(new SqlParameter("@resultado", SqlDbType.TinyInt)
            { Value = (byte)wb.Resultado });
            command.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 500)
            { Value = (object?)wb.Motivo ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@solicitudId", SqlDbType.UniqueIdentifier)
            { Value = wb.SolicitudId });
        }
        else
        {
            command.Parameters.Add(new SqlParameter("@numeroPedido", SqlDbType.NVarChar, 50)
            { Value = wb.NumeroPedido });
        }
    }
}
