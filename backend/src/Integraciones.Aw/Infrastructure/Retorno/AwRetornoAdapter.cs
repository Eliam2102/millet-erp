using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Retorno;

/// <summary>
/// <see cref="IAwRetornoPort"/> sobre <c>dbo.aw_retorno</c>. El mismo SQL corre en SQL Server (Real) y en
/// PostgreSQL (Demo, tools/aw-origen-demo/schema.sql); solo cambia el candado por pedido (<paramref name="sqlCandado"/>).
/// Solo INSERT y SELECT: el ERP no actualiza ni borra filas (script 14). El candado serializa por pedido la
/// lectura de la secuencia y el INSERT; UNIQUE(numero_pedido, secuencia) es la red de seguridad.
/// </summary>
public sealed class AwRetornoAdapter(
    IIntegracionSqlConnectionFactory fabrica, IOptions<AwPedidosOptions> options, string sqlCandado) : IAwRetornoPort
{
    /// <summary>SQL Server: candado de aplicación hasta el fin de la transacción.</summary>
    public const string CandadoSqlServer =
        "EXEC sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000";
    /// <summary>PostgreSQL: advisory lock transaccional.</summary>
    public const string CandadoPostgres = "SELECT pg_advisory_xact_lock(hashtextextended(@recurso, 0))";

    // Hay fila del mismo UUID + tipo si no está desfasada (pendiente, aplicada o rechazada: TI la sigue) o si
    // ya tiene esta versión o una mayor (reentrega del evento). Una desfasada con versión menor se reemplaza.
    private const string SqlExiste = """
        SELECT COUNT(*) FROM dbo.aw_retorno
         WHERE numero_pedido = @pedido AND tipo = 3 AND uuid = @uuid AND tipo_documento = @tipoDoc
           AND (resultado IS NULL OR resultado <> 3 OR version_pedido >= @version)
        """;
    private const string SqlSecuencia = "SELECT COALESCE(MAX(secuencia), 0) + 1 FROM dbo.aw_retorno WHERE numero_pedido = @pedido";
    private const string SqlInsertar = """
        INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, uuid, importe)
        VALUES (@pedido, @secuencia, 3, @version, @tipoDoc, @folio, @uuid, @importe)
        """;
    private const string SqlAcuses = """
        SELECT r.numero_pedido, r.secuencia, r.version_pedido, r.tipo_documento, r.folio, r.uuid, r.importe,
               r.aplicada_at, r.resultado, r.motivo, r.intentos
          FROM dbo.aw_retorno r
         WHERE r.tipo = 3
           AND ((r.resultado = 3 AND NOT EXISTS (SELECT 1 FROM dbo.aw_retorno n
                    WHERE n.numero_pedido = r.numero_pedido AND n.tipo = 3 AND n.uuid = r.uuid
                      AND n.tipo_documento = r.tipo_documento AND n.secuencia > r.secuencia))
                OR (r.resultado = 2 AND r.intentos >= 3))
         ORDER BY r.numero_pedido, r.secuencia
        """;

    public async Task<int?> AgregarDocumentoAsync(AwRetornoDocumento documento, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        await using (var candado = Comando(cn, tx, sqlCandado))
        {
            P(candado, "@recurso", DbType.String, $"aw_retorno:{documento.NumeroPedido}");
            await candado.ExecuteNonQueryAsync(ct);
        }

        await using (var existe = Comando(cn, tx, SqlExiste))
        {
            P(existe, "@pedido", DbType.String, documento.NumeroPedido);
            P(existe, "@uuid", DbType.String, documento.Uuid);
            P(existe, "@tipoDoc", DbType.String, documento.TipoDocumento);
            P(existe, "@version", DbType.Int64, documento.VersionPedido);
            if (Convert.ToInt32(await existe.ExecuteScalarAsync(ct)) > 0)
            {
                await tx.RollbackAsync(ct);
                return null;
            }
        }

        int secuencia;
        await using (var sec = Comando(cn, tx, SqlSecuencia))
        {
            P(sec, "@pedido", DbType.String, documento.NumeroPedido);
            secuencia = Convert.ToInt32(await sec.ExecuteScalarAsync(ct));
        }

        await using (var ins = Comando(cn, tx, SqlInsertar))
        {
            P(ins, "@pedido", DbType.String, documento.NumeroPedido);
            P(ins, "@secuencia", DbType.Int32, secuencia);
            P(ins, "@version", DbType.Int64, documento.VersionPedido);
            P(ins, "@tipoDoc", DbType.String, documento.TipoDocumento);
            P(ins, "@folio", DbType.String, documento.Folio);
            P(ins, "@uuid", DbType.String, documento.Uuid);
            P(ins, "@importe", DbType.Decimal, documento.Importe);
            await ins.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return secuencia;
    }

    public async Task<IReadOnlyList<AwRetornoAcuse>> LeerAcusesPorAtenderAsync(CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = Comando(cn, null, SqlAcuses);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<AwRetornoAcuse>();
        while (await r.ReadAsync(ct))
            lista.Add(new AwRetornoAcuse(
                r.GetString(0), r.GetInt32(1), r.GetInt64(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetDecimal(6),
                r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7),
                r.IsDBNull(8) ? null : Convert.ToByte(r.GetValue(8)),
                r.IsDBNull(9) ? null : r.GetString(9), r.GetInt32(10)));
        return lista;
    }

    private async Task<DbConnection> AbrirAsync(CancellationToken ct)
    {
        var cn = fabrica.CreateConnection();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.SqlConnectTimeoutSeconds));
        await cn.OpenAsync(timeout.Token);
        return cn;
    }

    private DbCommand Comando(DbConnection cn, DbTransaction? tx, string sql)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = options.Value.SqlQueryTimeoutSeconds;
        return cmd;
    }

    private static void P(DbCommand cmd, string nombre, DbType tipo, object? valor)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nombre;
        p.DbType = tipo;
        p.Value = valor ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
