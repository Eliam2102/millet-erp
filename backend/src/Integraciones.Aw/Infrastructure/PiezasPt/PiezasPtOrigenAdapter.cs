using System.Data.Common;
using System.Globalization;
using Millet.Integraciones.Aw.Application.PiezasPt;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

/// <summary>
/// Lector SOLO SELECT de los escaneos de pieza terminada (alcimdb, A+W Production). Un único adaptador para el
/// origen Demo (PostgreSQL) y el Real (SQL Server). Todos los valores viajan como parámetros; las estaciones
/// vienen de configuración. El ERP nunca escribe ni confirma nada en A+W.
/// </summary>
public sealed class PiezasPtOrigenAdapter(Func<DbConnection> fabrica, bool postgres) : IPiezasPtOrigenReader
{
    internal const string Columnas = "b.etikettnr, b.bearb_zeit, b.auftnr, b.pos, b.u_pos, b.teile_nr, b.fertig, b.esnr_ist, p.ae_intpos, pb.text1";
    internal const string Desde =
        "FROM dbo.awbar_bearbeit b " +
        "LEFT JOIN dbo.pool_pos p ON p.auftnr = b.auftnr AND p.pos = b.pos AND p.u_pos = b.u_pos " +
        // Llave de la consulta de referencia de TI (27-oct): pieza (teile_nr) y operación (bearb_nr); un renglón de awbar_bearbeit = una fila.
        "LEFT JOIN dbo.pool_bearbeit pb ON pb.auftnr = b.auftnr AND pb.pos = b.pos AND pb.u_pos = b.u_pos AND pb.teile_nr = b.teile_nr AND pb.bearb_nr = b.bearb_nr";
    // Solo Demo: en Real la clave de sucursal vive en MILMAIN OR_AVBEREICH (otra instancia).
    internal const string DesdeClaveDemo = " LEFT JOIN dbo.or_avbereich o ON o.auftnr = b.auftnr";
    // Jorge (SRC-114, 29-oct): pieza terminada = etiqueta de 9 caracteres con fertig = 1 y bruch_flag = 0 (la rotura entra como etiqueta+01 con bruch_flag = 1).
    internal const string Filtro = " WHERE b.fertig = 1 AND b.bruch_flag = 0 AND LEN(b.etikettnr) = 9 AND b.bearb_zeit >= @desde";
    // Real: servidor vinculado de TI (SRC-114), OPENQUERY para que el filtro se resuelva en producción. OPENQUERY no admite parámetros.
    internal const string ServidorVinculado = "AWPROD_ERP";
    internal const string Orden = " ORDER BY b.bearb_zeit, b.etikettnr";

    public Task<string> OrigenActualAsync(CancellationToken ct) => Task.FromResult(postgres ? "Demo" : "Real");

    internal static string ConstruirSql(bool postgres, int rangos, bool cursor)
    {
        var sql = ConstruirSqlLocal(postgres, rangos, cursor);
        // PostgreSQL no tiene LEN().
        return postgres ? sql.Replace("LEN(", "length(") : sql;
    }

    private static string ConstruirSqlLocal(bool postgres, int rangos, bool cursor)
    {
        var sql = new System.Text.StringBuilder("SELECT ");
        if (!postgres) sql.Append("TOP (@max) ");
        // Real sin clave de sucursal: alcimdb no la tiene; PT usa la del pedido, importada de MILMAIN OR_AVBEREICH (Jorge E08-4).
        sql.Append(Columnas).Append(postgres ? ", o.avbereich " : ", NULL AS avbereich ").Append(Desde);
        if (postgres) sql.Append(DesdeClaveDemo);
        sql.Append(Filtro).Append(" AND (")
           .Append(string.Join(" OR ", Enumerable.Range(0, rangos).Select(i => $"b.esnr_ist BETWEEN @d{i} AND @h{i}"))).Append(')');
        if (cursor)
            sql.Append(postgres
                ? " AND (b.bearb_zeit, b.etikettnr) > (@t, @e)"
                : " AND (b.bearb_zeit > @t OR (b.bearb_zeit = @t AND b.etikettnr > @e))");
        sql.Append(Orden);
        if (postgres) sql.Append(" LIMIT @max");
        return sql.ToString();
    }

    /// <summary>
    /// SQL del canal Real: la misma consulta, con los valores como literales validados (OPENQUERY no admite parámetros)
    /// y los objetos calificados con alcimdb. Solo SELECT.
    /// </summary>
    internal static string ConstruirSqlReal(DateTime desde, (DateTime Tiempo, string Etiqueta)? despuesDe,
        IReadOnlyList<(int Desde, int Hasta)> estaciones, int max)
    {
        var valores = new Dictionary<string, string>
        {
            ["desde"] = Fecha(desde), ["max"] = max.ToString(CultureInfo.InvariantCulture),
        };
        for (var i = 0; i < estaciones.Count; i++)
        {
            valores[$"d{i}"] = estaciones[i].Desde.ToString(CultureInfo.InvariantCulture);
            valores[$"h{i}"] = estaciones[i].Hasta.ToString(CultureInfo.InvariantCulture);
        }
        if (despuesDe is { } cur)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(cur.Etiqueta, "^[A-Za-z0-9]{1,50}$"))
                throw new ArgumentException("Etiqueta de cursor inválida.", nameof(despuesDe));
            valores["t"] = Fecha(cur.Tiempo); valores["e"] = $"'{cur.Etiqueta}'";
        }
        var interna = System.Text.RegularExpressions.Regex.Replace(ConstruirSqlLocal(false, estaciones.Count, despuesDe is not null), @"@(\w+)", m => valores[m.Groups[1].Value]);
        interna = System.Text.RegularExpressions.Regex.Replace(interna, @"\bdbo\.", "alcimdb.dbo.");
        return $"SELECT * FROM OPENQUERY([{ServidorVinculado}], '{interna.Replace("'", "''")}')";
    }

    private static string Fecha(DateTime t) => $"'{t.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)}'";

    public async Task<IReadOnlyList<EscaneoOrigen>> LeerAsync(DateTime desde, (DateTime Tiempo, string Etiqueta)? despuesDe,
        IReadOnlyList<(int Desde, int Hasta)> estaciones, int max, CancellationToken ct)
    {
        if (estaciones.Count == 0) return [];
        await using var c = fabrica();
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandTimeout = 30;
        if (postgres)
        {
            cmd.CommandText = ConstruirSql(true, estaciones.Count, despuesDe is not null);
            void Param(string n, object v) { var p = cmd.CreateParameter(); p.ParameterName = n; p.Value = v; cmd.Parameters.Add(p); }
            Param("desde", desde); Param("max", max);
            for (var i = 0; i < estaciones.Count; i++) { Param($"d{i}", estaciones[i].Desde); Param($"h{i}", estaciones[i].Hasta); }
            if (despuesDe is { } cur) { Param("t", cur.Tiempo); Param("e", cur.Etiqueta); }
        }
        else cmd.CommandText = ConstruirSqlReal(desde, despuesDe, estaciones, max);

        var filas = new List<EscaneoOrigen>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string? Texto(int i) => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            int? Entero(int i) => reader.IsDBNull(i) ? null : Convert.ToInt32(reader.GetValue(i), CultureInfo.InvariantCulture);
            filas.Add(new(Texto(0)!, Texto(2)!, Entero(8), Entero(4), Texto(5), Entero(7) ?? 0,
                Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture) == 1, Texto(9),
                Convert.ToDateTime(reader.GetValue(1), CultureInfo.InvariantCulture), Texto(10)));
        }
        return filas;
    }
}
