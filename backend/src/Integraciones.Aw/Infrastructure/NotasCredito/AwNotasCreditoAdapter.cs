using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.NotasCredito;

/// <summary>
/// <see cref="IAwNotasCreditoPort"/> sobre <c>dbo.aw_solicitud_nota_credito</c> (script 17, 86e3wjt5r). El mismo SQL
/// corre en SQL Server (Real, apagado salvo configuración) y en PostgreSQL (Demo, tools/aw-origen-demo/schema.sql).
/// Pendiente = <c>resultado IS NULL</c>: Aplicada y Rechazada son finales (A+W corrige con una versión nueva).
/// El ERP solo toca sus columnas (erp_nota_credito_id, estado_erp, resultado, motivo, procesada_at).
/// </summary>
public sealed class AwNotasCreditoAdapter(IIntegracionSqlConnectionFactory fabrica, IOptions<AwPedidosOptions> options)
    : IAwNotasCreditoPort
{
    private const string SqlPendientes = """
        SELECT solicitud_id, numero_nota, version, numero_pedido, factura_folio, cliente_ref, tipo_nota, importe,
               descripcion, creada_at
          FROM dbo.aw_solicitud_nota_credito
         WHERE resultado IS NULL
         ORDER BY numero_nota, version
        OFFSET 0 ROWS FETCH NEXT @max ROWS ONLY
        """;

    // COALESCE: el claim (erp_nota_credito_id) nunca se degrada a NULL.
    private const string SqlAcuse = """
        UPDATE dbo.aw_solicitud_nota_credito
           SET erp_nota_credito_id = COALESCE(@erpId, erp_nota_credito_id),
               estado_erp          = COALESCE(@estado, estado_erp),
               resultado           = @resultado,
               motivo              = @motivo,
               procesada_at        = @procesadaAt
         WHERE solicitud_id = @solicitudId
        """;

    public async Task<IReadOnlyList<SolicitudNotaCreditoAw>> LeerNotasPendientesAsync(int max, CancellationToken cancellationToken)
    {
        await using var cn = await AbrirAsync(cancellationToken);
        await using var cmd = PgPlumbing.CrearCommand(cn, SqlPendientes, options.Value);
        PgPlumbing.Param(cmd, "@max", DbType.Int32, max);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var lista = new List<SolicitudNotaCreditoAw>();
        while (await r.ReadAsync(cancellationToken))
            lista.Add(new SolicitudNotaCreditoAw(
                r.GetGuid(0), r.GetString(1), r.GetInt64(2), r.GetString(3), PgPlumbing.GetStringOrNull(r, 4),
                r.GetString(5), r.GetString(6), r.GetDecimal(7), PgPlumbing.GetStringOrNull(r, 8),
                r.GetFieldValue<DateTimeOffset>(9)));
        return lista;
    }

    public async Task EscribirAcuseAsync(AcuseNotaCreditoAw acuse, CancellationToken cancellationToken)
    {
        await using var cn = await AbrirAsync(cancellationToken);
        await using var cmd = PgPlumbing.CrearCommand(cn, SqlAcuse, options.Value);
        PgPlumbing.Param(cmd, "@erpId", DbType.Guid, acuse.ErpNotaCreditoId);
        PgPlumbing.Param(cmd, "@estado", DbType.String, acuse.Estado);
        PgPlumbing.Param(cmd, "@resultado", DbType.Int16, (short)acuse.Resultado);
        PgPlumbing.Param(cmd, "@motivo", DbType.String, acuse.Motivo is { Length: > 500 } m ? m[..500] : acuse.Motivo);
        PgPlumbing.Param(cmd, "@procesadaAt", DbType.DateTimeOffset, DateTimeOffset.UtcNow);
        PgPlumbing.Param(cmd, "@solicitudId", DbType.Guid, acuse.SolicitudId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<DbConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        var cn = fabrica.CreateConnection();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.SqlConnectTimeoutSeconds));
        await cn.OpenAsync(timeout.Token);
        return cn;
    }
}
