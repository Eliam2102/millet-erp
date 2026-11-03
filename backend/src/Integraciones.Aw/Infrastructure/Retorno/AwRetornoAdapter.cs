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
/// Solo INSERT y SELECT: el ERP no actualiza ni borra filas (script 14). Tipos 3 (Documento) y 4 (Pago, v3). El candado serializa por pedido la
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
    // Un solo INSERT para todos los tipos: cada tipo llena sus columnas y deja nulas las demás (los CHECK del script 14 cuidan la forma).
    private const string SqlInsertar = """
        INSERT INTO dbo.aw_retorno (numero_pedido, secuencia, tipo, version_pedido, tipo_documento, folio, uuid, importe, forma_pago, moneda)
        VALUES (@pedido, @secuencia, @tipo, @version, @tipoDoc, @folio, @uuid, @importe, @formaPago, @moneda)
        """;
    private const string SqlExisteFactura = """
        SELECT COUNT(*) FROM dbo.aw_retorno
         WHERE numero_pedido = @pedido AND tipo = 3 AND tipo_documento = 'FacturaFinal' AND folio = @folio
        """;
    // Última fila de cada grupo forma + moneda de la factura: es la que vale para TI (la nueva sustituye).
    private const string SqlUltimosPagos = """
        SELECT r.forma_pago, r.moneda, r.importe, r.resultado, r.version_pedido
          FROM dbo.aw_retorno r
         WHERE r.numero_pedido = @pedido AND r.tipo = 4 AND r.folio = @folio
           AND r.secuencia = (SELECT MAX(x.secuencia) FROM dbo.aw_retorno x
                               WHERE x.numero_pedido = r.numero_pedido AND x.tipo = 4 AND x.folio = r.folio
                                 AND x.forma_pago = r.forma_pago AND x.moneda = r.moneda)
        """;
    // «Sin reemplazo» = no hay fila posterior con la misma llave del tipo (documento: uuid + tipo; pago: folio + forma + moneda).
    private const string SqlAcuses = """
        SELECT r.numero_pedido, r.secuencia, r.version_pedido, r.tipo_documento, r.folio, r.uuid, r.importe,
               r.aplicada_at, r.resultado, r.motivo, r.intentos, r.tipo, r.forma_pago, r.moneda
          FROM dbo.aw_retorno r
         WHERE (r.resultado = 3 AND NOT EXISTS (SELECT 1 FROM dbo.aw_retorno n
                    WHERE n.numero_pedido = r.numero_pedido AND n.tipo = r.tipo AND n.secuencia > r.secuencia
                      AND COALESCE(n.uuid, '') = COALESCE(r.uuid, '')
                      AND COALESCE(n.tipo_documento, '') = COALESCE(r.tipo_documento, '')
                      AND COALESCE(n.folio, '') = COALESCE(r.folio, '')
                      AND COALESCE(n.forma_pago, '') = COALESCE(r.forma_pago, '')
                      AND COALESCE(n.moneda, '') = COALESCE(r.moneda, '')
                      AND COALESCE(n.estatus_destino, 0) = COALESCE(r.estatus_destino, 0)))
            OR (r.resultado = 2 AND r.intentos >= 3)
         ORDER BY r.numero_pedido, r.secuencia
        """;

    public async Task<int?> AgregarDocumentoAsync(AwRetornoDocumento documento, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        await CandadoAsync(cn, tx, documento.NumeroPedido, ct);

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

        var secuencia = await SiguienteSecuenciaAsync(cn, tx, documento.NumeroPedido, ct);
        await InsertarAsync(cn, tx, documento.NumeroPedido, secuencia, AwRetornoAcuse.TipoDocumentoFila, documento.VersionPedido,
            documento.TipoDocumento, documento.Folio, documento.Uuid, documento.Importe, null, null, ct);

        await tx.CommitAsync(ct);
        return secuencia;
    }

    public async Task<int> AgregarPagosAsync(string numeroPedido, long versionPedido, string folio,
        IReadOnlyList<AwPagoGrupo> grupos, bool exigirDocumento, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await CandadoAsync(cn, tx, numeroPedido, ct);

        if (exigirDocumento)
        {
            await using var existe = Comando(cn, tx, SqlExisteFactura);
            P(existe, "@pedido", DbType.String, numeroPedido);
            P(existe, "@folio", DbType.String, folio);
            // Sin su FacturaFinal no se escribe nada: el evento de factura timbrada recalcula los pagos al llegar.
            if (Convert.ToInt32(await existe.ExecuteScalarAsync(ct)) == 0)
            {
                await tx.RollbackAsync(ct);
                return 0;
            }
        }

        var ultimos = new Dictionary<(string, string), (decimal Importe, byte? Resultado, long Version)>();
        await using (var cmd = Comando(cn, tx, SqlUltimosPagos))
        {
            P(cmd, "@pedido", DbType.String, numeroPedido);
            P(cmd, "@folio", DbType.String, folio);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                ultimos[(r.GetString(0), r.GetString(1))] = (r.GetDecimal(2),
                    r.IsDBNull(3) ? null : Convert.ToByte(r.GetValue(3)), r.GetInt64(4));
        }

        // Estado deseado: los grupos actuales y, en 0, los que ya no existen (cobro cancelado).
        // La columna es decimal(18,2): se compara y se guarda ya redondeado, o cada recálculo parecería un cambio.
        var deseado = grupos.ToDictionary(g => (g.FormaPago, g.Moneda), g => Math.Round(g.Importe, 2, MidpointRounding.AwayFromZero));
        foreach (var (llave, ultimo) in ultimos)
            if (!deseado.ContainsKey(llave) && (ultimo.Importe != 0m || (ultimo.Resultado == 3 && ultimo.Version < versionPedido)))
                deseado[llave] = 0m;

        var secuencia = await SiguienteSecuenciaAsync(cn, tx, numeroPedido, ct);
        var agregadas = 0;
        foreach (var ((forma, moneda), importe) in deseado.OrderBy(d => d.Key.Item1).ThenBy(d => d.Key.Item2))
        {
            // Igual que el documento: no se repite si ya está el mismo importe, salvo que esa fila quedó desfasada con versión menor.
            if (ultimos.TryGetValue((forma, moneda), out var u) && u.Importe == importe
                && (u.Resultado != 3 || u.Version >= versionPedido))
                continue;
            await InsertarAsync(cn, tx, numeroPedido, secuencia + agregadas, AwRetornoAcuse.TipoPagoFila, versionPedido,
                null, folio, null, importe, forma, moneda, ct);
            agregadas++;
        }

        await tx.CommitAsync(ct);
        return agregadas;
    }

    private async Task CandadoAsync(DbConnection cn, DbTransaction tx, string numeroPedido, CancellationToken ct)
    {
        await using var candado = Comando(cn, tx, sqlCandado);
        P(candado, "@recurso", DbType.String, $"aw_retorno:{numeroPedido}");
        await candado.ExecuteNonQueryAsync(ct);
    }

    private async Task<int> SiguienteSecuenciaAsync(DbConnection cn, DbTransaction tx, string numeroPedido, CancellationToken ct)
    {
        await using var sec = Comando(cn, tx, SqlSecuencia);
        P(sec, "@pedido", DbType.String, numeroPedido);
        return Convert.ToInt32(await sec.ExecuteScalarAsync(ct));
    }

    private async Task InsertarAsync(DbConnection cn, DbTransaction tx, string numeroPedido, int secuencia, byte tipo, long version,
        string? tipoDocumento, string? folio, string? uuid, decimal? importe, string? formaPago, string? moneda, CancellationToken ct)
    {
        await using var ins = Comando(cn, tx, SqlInsertar);
        P(ins, "@pedido", DbType.String, numeroPedido);
        P(ins, "@secuencia", DbType.Int32, secuencia);
        P(ins, "@tipo", DbType.Int16, (short)tipo);
        P(ins, "@version", DbType.Int64, version);
        P(ins, "@tipoDoc", DbType.String, tipoDocumento);
        P(ins, "@folio", DbType.String, folio);
        P(ins, "@uuid", DbType.String, uuid);
        P(ins, "@importe", DbType.Decimal, importe);
        P(ins, "@formaPago", DbType.String, formaPago);
        P(ins, "@moneda", DbType.String, moneda);
        await ins.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AwRetornoAcuse>> LeerAcusesPorAtenderAsync(CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = Comando(cn, null, SqlAcuses);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<AwRetornoAcuse>();
        while (await r.ReadAsync(ct))
            lista.Add(new AwRetornoAcuse(
                r.GetString(0), r.GetInt32(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetDecimal(6),
                r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7),
                r.IsDBNull(8) ? null : Convert.ToByte(r.GetValue(8)),
                r.IsDBNull(9) ? null : r.GetString(9), r.GetInt32(10),
                Convert.ToByte(r.GetValue(11)), r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : r.GetString(13)));
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
