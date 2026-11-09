using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.TipoCambio;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.TipoCambio;

/// <summary>
/// Adaptador dual (SQL Server / PostgreSQL) para la tabla puente dbo.erp_tipo_cambio y consulta de A+W (I13.2).
/// Solo INSERT y SELECT: el ERP no actualiza ni borra filas. El candado serializa por moneda la lectura
/// de la versión y la inserción; PRIMARY KEY (moneda, fecha_aplicacion, version) es la red de seguridad.
/// </summary>
public sealed class AwTipoCambioAdapter(
    IIntegracionSqlConnectionFactory fabrica,
    IOptions<AwPedidosOptions> options,
    string sqlCandado) : IAwTipoCambioPort
{
    /// <summary>SQL Server: candado de aplicación hasta el fin de la transacción.</summary>
    public const string CandadoSqlServer =
        "EXEC sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000";

    /// <summary>PostgreSQL: advisory lock transaccional.</summary>
    public const string CandadoPostgres = "SELECT pg_advisory_xact_lock(hashtextextended(@recurso, 0))";

    public const string SqlUltimaVersion = """
        SELECT version, valor FROM dbo.erp_tipo_cambio
         WHERE moneda = @moneda AND fecha_aplicacion = @fecha
           AND version = (SELECT MAX(x.version) FROM dbo.erp_tipo_cambio x WHERE x.moneda = @moneda AND x.fecha_aplicacion = @fecha)
        """;

    public const string SqlInsertar = """
        INSERT INTO dbo.erp_tipo_cambio (moneda, fecha_aplicacion, version, valor, fecha_fix, origen)
        VALUES (@moneda, @fecha, @version, @valor, @fechaFix, @origen)
        """;

    public const string SqlAcuses = """
        SELECT r.moneda, r.fecha_aplicacion, r.version, r.valor, r.aplicada_at, r.resultado, r.motivo, r.intentos
          FROM dbo.erp_tipo_cambio r
         WHERE r.fecha_aplicacion >= @desde
           AND r.version = (SELECT MAX(x.version) FROM dbo.erp_tipo_cambio x
                             WHERE x.moneda = r.moneda AND x.fecha_aplicacion = r.fecha_aplicacion)
         ORDER BY r.moneda, r.fecha_aplicacion
        """;

    public const string SqlTipoCambioVigenteAw = """
        SELECT valutakurs FROM sysadm.ka_waehrungen WHERE waehrung = @moneda
        """;

    /// <summary>
    /// ISO del ERP → valor de A+W (KA_WAEHRUNGEN.WAEHRUNG: USD, Euro, CAN) con <see cref="AwPedidosOptions.MapeoMonedaAw"/>,
    /// la misma equivalencia que usa aw_retorno. Sin equivalencias configuradas se deja el ISO tal cual.
    /// </summary>
    private string MonedaAw(string moneda)
    {
        var iso = moneda.Trim().ToUpperInvariant();
        var mapeo = options.Value.MapeoMonedaAw;
        if (mapeo.Count == 0) return iso;
        return mapeo.TryGetValue(iso, out var aw)
            ? aw
            : throw new InvalidOperationException($"Sin equivalencia de moneda «{iso}» hacia A+W (IntegracionesAw:MapeoMonedaAw).");
    }

    /// <summary>Valor de A+W leído de la tabla puente → ISO del ERP (inverso de <see cref="MonedaAw"/>).</summary>
    private string MonedaErp(string valorAw)
    {
        var v = valorAw.Trim();
        var par = options.Value.MapeoMonedaAw.FirstOrDefault(kv => string.Equals(kv.Value, v, StringComparison.OrdinalIgnoreCase));
        return par.Key is null ? v.ToUpperInvariant() : par.Key.ToUpperInvariant();
    }

    public static string RecursoCandado(string moneda) =>
        $"erp_tipo_cambio:{moneda?.Trim().ToUpperInvariant()}";

    public async Task<int?> PublicarFilaAsync(AwTipoCambioFila fila, CancellationToken ct = default)
    {
        var moneda = fila.Moneda.Trim().ToUpperInvariant();
        var monedaAw = MonedaAw(moneda);
        await using var cn = await AbrirAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        await CandadoAsync(cn, tx, moneda, ct);

        int? maxVersion = null;
        decimal? valorExistente = null;

        await using (var cmd = Comando(cn, tx, SqlUltimaVersion))
        {
            P(cmd, "@moneda", DbType.String, monedaAw);
            P(cmd, "@fecha", DbType.Date, fila.FechaAplicacion);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                maxVersion = reader.GetInt32(0);
                valorExistente = reader.GetDecimal(1);
            }
        }

        int targetVersion;
        if (maxVersion.HasValue && valorExistente.HasValue)
        {
            if (valorExistente.Value == fila.Valor)
            {
                await tx.RollbackAsync(ct);
                return null;
            }
            targetVersion = maxVersion.Value + 1;
        }
        else
        {
            targetVersion = 1;
        }

        await using (var ins = Comando(cn, tx, SqlInsertar))
        {
            P(ins, "@moneda", DbType.String, monedaAw);
            P(ins, "@fecha", DbType.Date, fila.FechaAplicacion);
            P(ins, "@version", DbType.Int32, targetVersion);
            P(ins, "@valor", DbType.Decimal, fila.Valor);
            P(ins, "@fechaFix", DbType.Date, fila.FechaFix);
            P(ins, "@origen", DbType.String, fila.Origen);
            await ins.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return targetVersion;
    }

    public async Task<IReadOnlyList<AwTipoCambioAcuse>> ObtenerUltimosAcusesAsync(
        IReadOnlyList<string> monedas,
        DateOnly desde,
        CancellationToken ct = default)
    {
        HashSet<string>? filtro = null;
        if (monedas is { Count: > 0 })
        {
            filtro = monedas
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToUpperInvariant())
                .ToHashSet();
        }

        await using var cn = await AbrirAsync(ct);
        await using var cmd = Comando(cn, null, SqlAcuses);
        P(cmd, "@desde", DbType.Date, desde);

        var lista = new List<AwTipoCambioAcuse>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var moneda = MonedaErp(r.GetString(0));
            if (filtro is not null && filtro.Count > 0 && !filtro.Contains(moneda))
            {
                continue;
            }

            var fecha = ReadDate(r, 1);
            var version = r.GetInt32(2);
            var valor = r.GetDecimal(3);
            var aplicadaAt = ReadNullableDateTimeOffset(r, 4);
            var resultado = r.IsDBNull(5) ? null : (byte?)Convert.ToByte(r.GetValue(5), CultureInfo.InvariantCulture);
            var motivo = r.IsDBNull(6) ? null : r.GetString(6);
            var intentos = r.GetInt32(7);

            lista.Add(new AwTipoCambioAcuse(
                moneda,
                fecha,
                version,
                valor,
                aplicadaAt,
                resultado,
                motivo,
                intentos));
        }

        return lista;
    }

    public async Task<decimal?> ObtenerTipoCambioVigenteAwAsync(string moneda, CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = Comando(cn, null, SqlTipoCambioVigenteAw);
        P(cmd, "@moneda", DbType.String, MonedaAw(moneda));
        var res = await cmd.ExecuteScalarAsync(ct);
        if (res is null or DBNull) return null;
        return Convert.ToDecimal(res, CultureInfo.InvariantCulture);
    }

    private async Task<DbConnection> AbrirAsync(CancellationToken ct)
    {
        var cn = fabrica.CreateConnection();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.SqlConnectTimeoutSeconds));
        await cn.OpenAsync(timeout.Token);
        return cn;
    }

    private async Task CandadoAsync(DbConnection cn, DbTransaction tx, string moneda, CancellationToken ct)
    {
        await using var candado = Comando(cn, tx, sqlCandado);
        P(candado, "@recurso", DbType.String, RecursoCandado(moneda));
        await candado.ExecuteNonQueryAsync(ct);
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
        p.Value = valor switch
        {
            null => DBNull.Value,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            _ => valor
        };
        cmd.Parameters.Add(p);
    }

    private static DateOnly ReadDate(DbDataReader r, int ordinal)
    {
        var val = r.GetValue(ordinal);
        return val switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => DateOnly.FromDateTime(Convert.ToDateTime(val, CultureInfo.InvariantCulture))
        };
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader r, int ordinal)
    {
        if (r.IsDBNull(ordinal)) return null;
        var val = r.GetValue(ordinal);
        return val switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            _ => DateTimeOffset.Parse(Convert.ToString(val, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)
        };
    }
}
