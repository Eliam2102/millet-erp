using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.ConsumoComponentes;

/// <summary>
/// Una fila del origen: un escaneo de pieza terminada en ensamblado o laminación con uno de sus componentes de
/// <c>pool_teile</c> (agrupado: <c>Capas</c> = cuántas veces viene) o ninguno, y el gas de la posición.
/// Medidas en mm.
/// </summary>
public sealed record FilaConsumoOrigen(string Etiqueta, DateTime BearbZeit, string Auftnr, int Pos, int Operacion,
    bool ConGas, string? GasArtnr, decimal? PosAnchoMm, decimal? PosAltoMm, string? CompArtnr, int Capas,
    decimal? CompAnchoMm, decimal? CompAltoMm, string? ClaveSucursal);

/// <summary>Lector de componentes por pieza terminada del origen activo (Demo o Real). Nunca escribe en A+W.</summary>
public interface IConsumoComponentesOrigenReader
{
    Task<string> OrigenActualAsync(CancellationToken ct);

    /// <summary>Filas con <c>bearb_zeit &gt;= desde</c>, ordenadas por (bearb_zeit, etikettnr); con cursor, desde ese escaneo inclusive.</summary>
    Task<IReadOnlyList<FilaConsumoOrigen>> LeerAsync(DateTime desde, (DateTime Tiempo, string Etiqueta)? desdeEscaneo, int max, CancellationToken ct);
}

/// <summary>
/// Lector SOLO SELECT de la composición de cada pieza terminada (A4.8, D1/D2), clon del patrón de
/// <c>PiezasPtOrigenAdapter</c>: Demo en PostgreSQL y Real por el servidor vinculado AWPROD_ERP con OPENQUERY; la
/// sucursal sale del pedido (MILMAIN <c>BW_AUFTR_KOPF.OR_AVBEREICH</c>, D7). Argón: <c>pool_pos.gas1_artnr</c> con
/// <c>gas_flag = 1</c>; separador (520xxx) y PVB (419xxx, bajo la capa laminada) de <c>pool_teile</c>.
/// </summary>
public sealed class ConsumoComponentesOrigenAdapter(Func<DbConnection> fabrica, bool postgres) : IConsumoComponentesOrigenReader
{
    internal const string ServidorVinculado = "AWPROD_ERP";

    // Jorge (SRC-114): pieza terminada = etiqueta de 9 caracteres, fertig = 1, bruch_flag = 0.
    internal const string Interna =
        "SELECT {top}b.etikettnr, b.bearb_zeit, b.auftnr, b.pos, b.art_nr, p.gas_flag, p.gas1_artnr, " +
        "p.breite AS pos_breite, p.hoehe AS pos_hoehe, t.artnr AS comp_artnr, COUNT(t.artnr) AS capas, " +
        "MAX(t.breite) AS comp_breite, MAX(t.hoehe) AS comp_hoehe{demo} " +
        "FROM dbo.awbar_bearbeit b " +
        "LEFT JOIN dbo.pool_pos p ON p.auftnr = b.auftnr AND p.pos = b.pos AND p.u_pos = b.u_pos " +
        "LEFT JOIN dbo.pool_teile t ON t.auftnr = b.auftnr AND t.pos = b.pos AND t.u_pos = b.u_pos AND (" +
        "(b.art_nr = 999970 AND t.artnr BETWEEN 520000 AND 529999) OR " +
        "(b.art_nr = 999980 AND t.oberteil_nr = b.teile_nr AND t.artnr BETWEEN 419000 AND 419999)){demojoin} " +
        "WHERE b.fertig = 1 AND b.bruch_flag = 0 AND LEN(b.etikettnr) = 9 AND b.art_nr IN (999970, 999980) AND b.bearb_zeit >= @desde{cursor} " +
        "GROUP BY b.etikettnr, b.bearb_zeit, b.auftnr, b.pos, b.art_nr, p.gas_flag, p.gas1_artnr, p.breite, p.hoehe, t.artnr{demo} " +
        "ORDER BY b.bearb_zeit, b.etikettnr, b.art_nr, t.artnr{limit}";

    public Task<string> OrigenActualAsync(CancellationToken ct) => Task.FromResult(postgres ? "Demo" : "Real");

    internal static string ConstruirSqlDemo(bool cursor) => Interna
        .Replace("{top}", "").Replace("{demo}", ", o.avbereich")
        .Replace("{demojoin}", " LEFT JOIN dbo.or_avbereich o ON o.auftnr = b.auftnr")
        .Replace("{cursor}", cursor ? " AND (b.bearb_zeit, b.etikettnr) >= (@t, @e)" : "")
        .Replace("{limit}", " LIMIT @max").Replace("LEN(", "length(");

    /// <summary>Canal Real: OPENQUERY no admite parámetros; los valores van como literales validados. Solo SELECT.</summary>
    internal static string ConstruirSqlReal(DateTime desde, (DateTime Tiempo, string Etiqueta)? cursor, int max)
    {
        var valores = new Dictionary<string, string> { ["desde"] = Fecha(desde), ["max"] = max.ToString(CultureInfo.InvariantCulture) };
        if (cursor is { } c)
        {
            if (!Regex.IsMatch(c.Etiqueta, "^[A-Za-z0-9]{1,50}$"))
                throw new ArgumentException("Etiqueta de cursor inválida.", nameof(cursor));
            valores["t"] = Fecha(c.Tiempo); valores["e"] = $"'{c.Etiqueta}'";
        }
        var interna = Interna.Replace("{top}", "TOP (@max) ").Replace("{demo}", "").Replace("{demojoin}", "").Replace("{limit}", "")
            .Replace("{cursor}", cursor is null ? "" : " AND (b.bearb_zeit > @t OR (b.bearb_zeit = @t AND b.etikettnr >= @e))");
        interna = Regex.Replace(interna, @"@(\w+)", m => valores[m.Groups[1].Value]);
        interna = Regex.Replace(interna, @"\bdbo\.", "alcimdb.dbo.");
        // La sucursal vive en MILMAIN (instancia de la conexión), no en alcimdb: se cruza fuera del OPENQUERY.
        return $"SELECT q.*, k.OR_AVBEREICH AS avbereich FROM OPENQUERY([{ServidorVinculado}], '{interna.Replace("'", "''")}') q " +
               "LEFT JOIN MILMAIN.SYSADM.BW_AUFTR_KOPF k ON CONVERT(varchar(20), k.ID) = CONVERT(varchar(20), q.auftnr) " +
               "ORDER BY q.bearb_zeit, q.etikettnr, q.art_nr, q.comp_artnr";
    }

    private static string Fecha(DateTime t) => $"'{t.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)}'";

    public async Task<IReadOnlyList<FilaConsumoOrigen>> LeerAsync(DateTime desde, (DateTime Tiempo, string Etiqueta)? desdeEscaneo, int max, CancellationToken ct)
    {
        await using var c = fabrica();
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandTimeout = 30;
        if (postgres)
        {
            cmd.CommandText = ConstruirSqlDemo(desdeEscaneo is not null);
            void Param(string n, object v) { var p = cmd.CreateParameter(); p.ParameterName = n; p.Value = v; cmd.Parameters.Add(p); }
            Param("desde", desde); Param("max", max);
            if (desdeEscaneo is { } cur) { Param("t", cur.Tiempo); Param("e", cur.Etiqueta); }
        }
        else cmd.CommandText = ConstruirSqlReal(desde, desdeEscaneo, max);

        var filas = new List<FilaConsumoOrigen>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            object? V(string n) { var i = r.GetOrdinal(n); return r.IsDBNull(i) ? null : r.GetValue(i); }
            string? Texto(string n) => V(n) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() : null;
            // A+W guarda las medidas en milésimas de mm.
            decimal? Mm(string n) => V(n) is { } v ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) / 1000m : null;
            filas.Add(new(Texto("etikettnr")!, Convert.ToDateTime(V("bearb_zeit"), CultureInfo.InvariantCulture), Texto("auftnr")!,
                Convert.ToInt32(V("pos"), CultureInfo.InvariantCulture), Convert.ToInt32(V("art_nr"), CultureInfo.InvariantCulture),
                V("gas_flag") is { } g && Convert.ToInt32(g, CultureInfo.InvariantCulture) == 1, Texto("gas1_artnr"),
                Mm("pos_breite"), Mm("pos_hoehe"), Texto("comp_artnr"), Convert.ToInt32(V("capas") ?? 0, CultureInfo.InvariantCulture),
                Mm("comp_breite"), Mm("comp_hoehe"), Texto("avbereich")));
        }
        return filas;
    }

    /// <summary>
    /// Componentes a descontar de una fila: argón (solo ensamblado, con gas), el componente de <c>pool_teile</c> o,
    /// si no trae ninguno, una marca sin componente (A4.8-a: solo se descuenta el vidrio y queda la excepción).
    /// </summary>
    public static IReadOnlyList<PiezaComponenteAw> Expandir(FilaConsumoOrigen f)
    {
        var lista = new List<PiezaComponenteAw>();
        PiezaComponenteAw Pieza(string comp, int capas, decimal? ancho, decimal? alto) =>
            new(f.Etiqueta, f.Operacion, comp, capas, f.Auftnr, f.Pos, ancho, alto, f.ClaveSucursal, f.BearbZeit);
        // ponytail: solo gas1 (una cámara); un triple con segunda cámara de gas necesita gas2_artnr cuando aparezca.
        if (f.Operacion == Millet.Almacen.Application.AppAlmacenes.ConsumoComponentes.OperacionEnsamblado && f.ConGas && f.GasArtnr is { Length: > 0 } gas && gas != "0")
            lista.Add(Pieza(gas, 1, f.PosAnchoMm, f.PosAltoMm));
        if (f.CompArtnr is { Length: > 0 } comp)
        {
            var propia = f.CompAnchoMm is > 0 && f.CompAltoMm is > 0;
            lista.Add(Pieza(comp, Math.Max(1, f.Capas), propia ? f.CompAnchoMm : f.PosAnchoMm, propia ? f.CompAltoMm : f.PosAltoMm));
        }
        if (lista.Count == 0) lista.Add(Pieza("", 1, f.PosAnchoMm, f.PosAltoMm));
        return lista;
    }
}

/// <summary>Elige el lector Demo o Real según el origen activo de A+W (mismo criterio que PT).</summary>
public sealed class ConsumoComponentesOrigenSelector(IServiceProvider services, AwOrigenSesion sesion) : IConsumoComponentesOrigenReader
{
    public async Task<string> OrigenActualAsync(CancellationToken ct) => await sesion.EsDemoAsync(ct) ? "Demo" : "Real";

    public async Task<IReadOnlyList<FilaConsumoOrigen>> LeerAsync(DateTime desde, (DateTime Tiempo, string Etiqueta)? desdeEscaneo, int max, CancellationToken ct) =>
        await (services.GetKeyedService<IConsumoComponentesOrigenReader>(await OrigenActualAsync(ct))
            ?? throw new BusinessRuleException("CONSUMO_ORIGEN_NO_HABILITADO",
                "Use Demo mientras no esté firmado I13.4; revise la conexión y la bandera de consumo de componentes."))
            .LeerAsync(desde, desdeEscaneo, max, ct);
}
