using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Productos;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>Fila comparable: referencia externa + campos clave ya normalizados (trim, vacío = null).</summary>
public sealed record AwFilaConciliable(string Referencia, IReadOnlyDictionary<string, string?> Campos)
{
    /// <summary>SHA-256 de los campos en orden alfabético; igual hash = misma fila a efectos de conciliación.</summary>
    public string Hash
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var (k, v) in Campos.OrderBy(c => c.Key, StringComparer.Ordinal))
                sb.Append(k).Append('=').Append(v is null ? "~" : $"{v.Length}:{v}").Append('|');
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }
    }
}

/// <summary>Resultado de conciliar origen vs destino, con las diferencias explícitas (no solo bool).</summary>
public sealed record AwInformeConciliacion(
    int ConteoOrigen, int ConteoDestino,
    IReadOnlyList<string> Faltantes, IReadOnlyList<string> Sobrantes, IReadOnlyList<string> Diferencias)
{
    public bool Conciliado => ConteoOrigen == ConteoDestino && Faltantes.Count == 0 && Sobrantes.Count == 0 && Diferencias.Count == 0;

    public override string ToString() =>
        $"origen={ConteoOrigen} destino={ConteoDestino}"
        + $"{Environment.NewLine}faltantes en destino ({Faltantes.Count}): [{string.Join(", ", Faltantes)}]"
        + $"{Environment.NewLine}sobrantes en destino ({Sobrantes.Count}): [{string.Join(", ", Sobrantes)}]"
        + $"{Environment.NewLine}diferencias ({Diferencias.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, Diferencias)}";

    /// <summary>Falla con el informe completo si no concilia.</summary>
    public void DebeConciliar() => Assert.True(Conciliado, ToString());
}

/// <summary>
/// Conciliación reutilizable origen (SQL sandbox, solo SELECT) vs destino (Postgres). <see cref="Comparar"/> y
/// <see cref="LeerSqlAsync"/> son genéricos (Productos los reutiliza con su propio destino);
/// los métodos *Clientes* conocen el mapeo de clientes.
/// </summary>
public static class AwConciliacion
{
    /// <summary>Referencias del sandbox por debajo de este valor; las de otras pruebas usan bases enormes.</summary>
    public const int MaxReferenciaSandbox = 100_000;

    public static AwInformeConciliacion Comparar(IReadOnlyCollection<AwFilaConciliable> origen, IReadOnlyCollection<AwFilaConciliable> destino)
    {
        var o = origen.ToDictionary(f => f.Referencia);
        var d = destino.ToDictionary(f => f.Referencia);
        var faltantes = o.Keys.Except(d.Keys).Order(StringComparer.Ordinal).ToList();
        var sobrantes = d.Keys.Except(o.Keys).Order(StringComparer.Ordinal).ToList();
        var difs = new List<string>();
        foreach (var r in o.Keys.Intersect(d.Keys).Order(StringComparer.Ordinal))
        {
            if (o[r].Hash == d[r].Hash) continue;
            foreach (var campo in o[r].Campos.Keys.Union(d[r].Campos.Keys).Order(StringComparer.Ordinal))
            {
                o[r].Campos.TryGetValue(campo, out var vo);
                d[r].Campos.TryGetValue(campo, out var vd);
                if (vo != vd) difs.Add($"  ref {r} campo {campo}: origen='{vo ?? "<null>"}' destino='{vd ?? "<null>"}'");
            }
        }
        return new(o.Count, d.Count, faltantes, sobrantes, difs);
    }

    /// <summary>Ejecuta un SELECT: la 1ª columna es la referencia; el resto se llaman como su alias. Normaliza trim/vacío=null.</summary>
    public static async Task<List<AwFilaConciliable>> LeerSqlAsync(string cadenaConexion, string sql)
    {
        await using var cn = new SqlConnection(cadenaConexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        var res = new List<AwFilaConciliable>();
        while (await r.ReadAsync())
        {
            var campos = new Dictionary<string, string?>();
            for (var i = 1; i < r.FieldCount; i++)
                campos[r.GetName(i)] = Norm(r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture));
            res.Add(new(Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!, campos));
        }
        return res;
    }

    public static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Clientes del sandbox (SYSADM.KU_KUNDEN, ID &gt; 0) con los campos clave que el sincronizador registra.</summary>
    public static async Task<List<AwFilaConciliable>> LeerOrigenClientesAsync(string cadenaConexion)
    {
        var crudo = await LeerSqlAsync(cadenaConexion, """
            SELECT CAST(ID AS nvarchar(20)) AS Ref, NAME1, NAME2, NAME3, MANDANT AS Mandant, WAEHRUNG AS Moneda,
                   ZAHLBED AS Condicion, KZ_STATUS AS Estado, KZ_GESPERRT AS Bloqueo, UST_ID AS UstId,
                   STRASSE AS Calle, ORT AS Ciudad
              FROM SYSADM.KU_KUNDEN WHERE ID > 0
            """);
        return crudo.Select(f =>
        {
            var c = f.Campos.Where(k => k.Key is not ("NAME1" or "NAME2" or "NAME3")).ToDictionary(k => k.Key, k => k.Value);
            // Igual que el mapper de producción: NAME1 NAME2 NAME3 limpios, unidos por espacio.
            c["Nombre"] = string.Join(' ', new[] { f.Campos["NAME1"], f.Campos["NAME2"], f.Campos["NAME3"] }.OfType<string>());
            return new AwFilaConciliable(f.Referencia, c);
        }).ToList();
    }

    /// <summary>Clientes Origen=Aw con referencia numérica ≤ <see cref="MaxReferenciaSandbox"/> + su registro de origen.</summary>
    public static async Task<List<AwFilaConciliable>> LeerDestinoClientesAsync(CompartidoDbContext db)
    {
        var clientes = (await db.Clientes.AsNoTracking().Where(c => c.Origen == OrigenMaster.Aw && c.ReferenciaExterna != null).ToListAsync())
            .Where(c => int.TryParse(c.ReferenciaExterna, out var n) && n <= MaxReferenciaSandbox).ToList();
        var ids = clientes.Select(c => c.Id).ToList();
        var regs = (await db.Set<ClienteSincronizacionAw>().AsNoTracking().Where(r => ids.Contains(r.ClienteId)).ToListAsync())
            .ToDictionary(r => r.ClienteId);
        return clientes.Where(c => regs.ContainsKey(c.Id)).Select(c =>
        {
            var r = regs[c.Id];
            return new AwFilaConciliable(c.ReferenciaExterna!, new Dictionary<string, string?>
            {
                ["Nombre"] = Norm(r.NombreComercialOrigen), ["Mandant"] = r.MandantOrigen?.ToString(CultureInfo.InvariantCulture),
                ["Moneda"] = Norm(r.MonedaCodigoOrigen), ["Condicion"] = Norm(r.CondicionCodigoOrigen),
                ["Estado"] = r.EstadoOrigenCrudo?.ToString(CultureInfo.InvariantCulture),
                ["Bloqueo"] = r.BloqueoOrigenCrudo?.ToString(CultureInfo.InvariantCulture),
                ["UstId"] = Norm(r.CandidatoFiscalUstId), ["Calle"] = Norm(r.DomicilioOrigenCalle), ["Ciudad"] = Norm(r.DomicilioOrigenCiudad),
            });
        }).ToList();
    }

    /// <summary>"0.###" invariante; igual que el hash de producción trata los números.</summary>
    public static string? Num(decimal? v) => v is > 0 ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// Productos del sandbox (BA_PRODUKT &gt; 0) con los campos que el sincronizador aplica, calculados aquí de forma
    /// independiente del lector (SQL directo): descripción con respaldo BA_MCODE, unidad normalizada, baja, medidas 0 = null
    /// y composición de nivel 1 (capas con espesor &gt; 0 unidas con '+'). <paramref name="tiposExcluidos"/> = BA_PRODUKTART a omitir.
    /// </summary>
    public static async Task<List<AwFilaConciliable>> LeerOrigenProductosAsync(string cadenaConexion, params string[] tiposExcluidos)
    {
        var crudo = await LeerSqlAsync(cadenaConexion, """
            SELECT CAST(p.BA_PRODUKT AS nvarchar(20)) AS Ref, p.BA_MCODE AS Mcode, p.BA_PRODUKTART AS Art, p.KZ_GESPERRT AS Bloqueo,
                   p.BA_MASS_DICKE AS Dicke, p.BA_STD_HOEHE AS Alto, p.BA_STD_BREITE AS Ancho,
                   b.BA_BEZ1 AS B1, b.BA_BEZ2 AS B2, b.BA_BEZ3 AS B3, b.BA_MENGENEINH AS Unidad
              FROM SYSADM.BA_PRODUKTE p LEFT JOIN SYSADM.BA_PRODUKTE_BEZ b ON b.BA_PRODUKT = p.BA_PRODUKT AND b.SPRACH_ID = 0
             WHERE p.BA_PRODUKT > 0
            """);
        var capas = (await LeerSqlAsync(cadenaConexion, """
            SELECT CAST(s.PRODUKT AS nvarchar(20)), s.BOM_POS AS Pos, c.BA_MASS_DICKE AS Dicke
              FROM SYSADM.BA_STUKL s JOIN SYSADM.BA_PRODUKTE c ON c.BA_PRODUKT = s.BOM_PRODUKT
             WHERE s.BOM_LEVEL = 1
            """)).GroupBy(f => f.Referencia).ToDictionary(g => g.Key,
            g => string.Join('+', g.OrderBy(f => int.Parse(f.Campos["Pos"]!, CultureInfo.InvariantCulture))
                .Select(f => Dec(f.Campos["Dicke"])).Where(d => d > 0).Select(d => Num(d)!)));
        return crudo.Where(f => f.Campos["Art"] is null || !tiposExcluidos.Contains(f.Campos["Art"])).Select(f =>
        {
            var c = f.Campos;
            var desc = string.Join(' ', new[] { c["B1"], c["B2"], c["B3"] }.OfType<string>());
            var comp = capas.GetValueOrDefault(f.Referencia);
            var (alto, ancho, esp) = (Num(Dec(c["Alto"])), Num(Dec(c["Ancho"])), Num(Dec(c["Dicke"])));
            var hayVariante = alto is not null || ancho is not null || esp is not null || !string.IsNullOrEmpty(comp);
            return new AwFilaConciliable(f.Referencia, new Dictionary<string, string?>
            {
                ["Descripcion"] = desc.Length > 0 ? desc : c["Mcode"],
                ["Unidad"] = AwProductoSnapshotMapper.NormalizarUnidad(c["Unidad"]),
                ["Baja"] = c["Bloqueo"] == "0" ? "0" : "1",
                ["Alto"] = alto, ["Ancho"] = ancho, ["Espesor"] = esp,
                ["Composicion"] = hayVariante && !string.IsNullOrEmpty(comp) ? comp : null,
            });
        }).ToList();
    }

    private static decimal? Dec(string? s) => s is null ? null : decimal.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Productos Aw con referencia numérica ≤ <see cref="MaxReferenciaSandbox"/> (variante BASE) en el mismo formato.</summary>
    public static async Task<List<AwFilaConciliable>> LeerDestinoProductosAsync(CompartidoDbContext db)
    {
        var ps = (await db.ProductosAw.AsNoTracking().Include(p => p.Variantes).Where(p => p.Origen == OrigenMaster.Aw).ToListAsync())
            .Where(p => int.TryParse(p.ReferenciaExterna, out var n) && n <= MaxReferenciaSandbox);
        return ps.Select(p =>
        {
            var v = p.Variantes.FirstOrDefault(x => x.ClaveVariante == "BASE");
            return new AwFilaConciliable(p.ReferenciaExterna, new Dictionary<string, string?>
            {
                ["Descripcion"] = Norm(p.Descripcion), ["Unidad"] = p.UnidadMedida, ["Baja"] = p.Estatus == Millet.Catalogos.Domain.EstatusCatalogo.Inactivo ? "1" : "0",
                ["Alto"] = Num(v?.AltoMm), ["Ancho"] = Num(v?.AnchoMm), ["Espesor"] = Num(v?.EspesorMm), ["Composicion"] = Norm(v?.Composicion),
            });
        }).ToList();
    }
}
