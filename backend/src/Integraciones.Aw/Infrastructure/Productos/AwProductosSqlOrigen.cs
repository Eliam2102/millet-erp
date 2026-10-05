using System.Text.RegularExpressions;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Productos;

/// <summary>
/// Lector SOLO-SELECT de productos A+W (<c>SYSADM.BA_PRODUKTE</c> + descripción <c>BA_PRODUKTE_BEZ</c> +
/// composición <c>BA_STUKL</c> nivel 1 + árbol completo de piezas + medidas de hoja entera <c>BA_LAGMA</c>). SQL Server 2016: sin STRING_AGG/STRING_SPLIT/JSON, por eso la
/// composición se arma en C#. Deshabilitado por defecto (ver <see cref="ProductosDependencyInjection"/>).
/// </summary>
public sealed class AwProductosSqlOrigen : IAwProductosOrigen
{
    private const string Columnas = """
        p.BA_PRODUKT, p.BA_MCODE, p.KZ_GESPERRT, p.BA_MASS_DICKE, p.BA_STD_HOEHE, p.BA_STD_BREITE,
        p.TRANSACTION_TIME, b.BA_BEZ1, b.BA_BEZ2, b.BA_BEZ3, b.BA_MENGENEINH, p.BA_PRODUKTGRP, p.BA_PRODUKTART,
        p.BA_WGR, w.BEZ
        """;

    private const string ColumnasInternas =
        "BA_PRODUKT, BA_MCODE, KZ_GESPERRT, BA_MASS_DICKE, BA_STD_HOEHE, BA_STD_BREITE, TRANSACTION_TIME, BA_PRODUKTGRP, BA_PRODUKTART, BA_WGR";

    internal const string MarcaTipos = "/*TIPOS*/";

    // El TOP va en la tabla derivada para que el lote cuente productos y no filas del join con BEZ.
    public const string SqlPagina =
        "SELECT " + Columnas + $"""

          FROM (SELECT TOP (@n) {ColumnasInternas}
                  FROM SYSADM.BA_PRODUKTE WHERE BA_PRODUKT > @cursor {MarcaTipos} ORDER BY BA_PRODUKT) AS p
          LEFT JOIN SYSADM.BA_PRODUKTE_BEZ AS b ON b.BA_PRODUKT = p.BA_PRODUKT AND b.SPRACH_ID = 0
          LEFT JOIN SYSADM.KA_WGR AS w ON w.ID = p.BA_WGR
         ORDER BY p.BA_PRODUKT
        """;

    public const string SqlReferencia =
        "SELECT " + Columnas + $"""

          FROM (SELECT TOP (1) {ColumnasInternas}
                  FROM SYSADM.BA_PRODUKTE WHERE BA_PRODUKT = @cursor ORDER BY BA_PRODUKT) AS p
          LEFT JOIN SYSADM.BA_PRODUKTE_BEZ AS b ON b.BA_PRODUKT = p.BA_PRODUKT AND b.SPRACH_ID = 0
          LEFT JOIN SYSADM.KA_WGR AS w ON w.ID = p.BA_WGR
         ORDER BY p.BA_PRODUKT
        """;

    // Capas de nivel 1 (BOM_LEVEL = 1) de los productos del rango de la página.
    public const string SqlComposicion = """
        SELECT s.PRODUKT, s.BOM_POS, c.BA_MASS_DICKE
          FROM SYSADM.BA_STUKL AS s
          JOIN SYSADM.BA_PRODUKTE AS c ON c.BA_PRODUKT = s.BOM_PRODUKT
         WHERE s.BOM_LEVEL = 1 AND s.PRODUKT BETWEEN @min AND @max
         ORDER BY s.PRODUKT, s.BOM_POS
        """;

    // Árbol completo (todos los niveles, incluye procesos/rellenos/perfiles que no están en el catálogo sincronizado).
    // BOM_ID da el orden; BOM_NODE = posición 1-based de la fila padre en ese orden (0 = raíz).
    public const string SqlComponentes = """
        SELECT s.PRODUKT, s.BOM_LEVEL, s.BOM_NODE, s.BOM_PRODUKT, c.BA_MASS_DICKE, c.BA_PRODUKTART, c.BA_MCODE,
               d.BA_BEZ1, d.BA_BEZ2, d.BA_BEZ3, w.BEA_TEXT
          FROM SYSADM.BA_STUKL AS s
          LEFT JOIN SYSADM.BA_PRODUKTE AS c ON c.BA_PRODUKT = s.BOM_PRODUKT
          LEFT JOIN SYSADM.BA_PRODUKTE_BEZ AS d ON d.BA_PRODUKT = s.BOM_PRODUKT AND d.SPRACH_ID = 0
          LEFT JOIN SYSADM.BA_STUKL_BEARB AS w ON w.PRODUKT = s.PRODUKT AND w.BOM_ID = s.BOM_ID
         WHERE s.PRODUKT BETWEEN @min AND @max
         ORDER BY s.PRODUKT, s.BOM_ID
        """;

    // Medidas de hoja entera (BA_BREITE = ancho, BA_HOEHE = alto): un código con varias medidas.
    public const string SqlMedidas = """
        SELECT BA_PRODUKT, BA_BREITE, BA_HOEHE
          FROM SYSADM.BA_LAGMA
         WHERE BA_PRODUKT BETWEEN @min AND @max
         ORDER BY BA_PRODUKT, BA_BREITE, BA_HOEHE
        """;

    private readonly IIntegracionSqlConnectionFactory _factory;
    private readonly AwPedidosOptions _plomeria; // solo timeouts, para reusar SqlPlumbing sin tocar Pedidos
    private readonly string[] _tiposExcluidos;
    private readonly ILogger<AwProductosSqlOrigen> _logger;

    public AwProductosSqlOrigen(
        IIntegracionSqlConnectionFactory factory,
        IOptions<AwProductosOptions> options,
        ILogger<AwProductosSqlOrigen> logger)
    {
        _factory = factory;
        _plomeria = new AwPedidosOptions
        {
            SqlQueryTimeoutSeconds = options.Value.SqlQueryTimeoutSeconds,
            SqlConnectTimeoutSeconds = options.Value.SqlConnectTimeoutSeconds,
        };
        _tiposExcluidos = options.Value.TiposExcluidos ?? [];
        _logger = logger;
    }

    public async Task<AwProductoOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct)
    {
        // BA_PRODUKT = 0 es el registro nulo de A+W ('<indf>' en todo); la lectura por páginas ya lo excluye (> 0).
        if (!int.TryParse(referencia, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) return null;
        var filas = await LeerAsync("LeerProductoPorReferencia", SqlReferencia, id, null, ct);
        return filas.FirstOrDefault();
    }

    public async Task<AwProductosPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
    {
        tamano = Math.Clamp(tamano, 1, AwProductosOptions.TamanoLoteMaximo);
        var desde = 0;
        if (cursor is not null && !int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out desde))
            throw new ArgumentException("Cursor inválido.", nameof(cursor));

        var filtro = _tiposExcluidos.Length == 0 ? ""
            : "AND BA_PRODUKTART NOT IN (" + string.Join(", ", _tiposExcluidos.Select((_, i) => "@t" + i)) + ")";
        var filas = await LeerAsync("LeerPaginaProductos", SqlPagina.Replace(MarcaTipos, filtro), desde, tamano, ct);
        return new(filas, filas.Count == tamano ? filas[^1].ProductoRef : null);
    }

    private Task<List<AwProductoOrigenFila>> LeerAsync(string operacion, string sql, int cursor, int? n, CancellationToken ct) =>
        SqlPlumbing.EjecutarAsync(_factory, _plomeria, _logger, operacion, async connection =>
        {
            // Consulta 1: productos de la página (el LEFT JOIN a BEZ no debería duplicar; si lo hiciera, gana la primera).
            var productos = new List<(int Id, string? Mcode, int Bloqueo, decimal? Dicke, decimal? Hoehe, decimal? Breite,
                DateTime? Tt, string? B1, string? B2, string? B3, string? Unidad, string? Grupo, string? Tipo, string? Wgr, string? WgrBez)>();
            using (var command = SqlPlumbing.CrearCommand(connection, sql, _plomeria))
            {
                command.Parameters.Add(new SqlParameter("@cursor", SqlDbType.Int) { Value = cursor });
                if (n is not null) command.Parameters.Add(new SqlParameter("@n", SqlDbType.Int) { Value = n });
                for (var i = 0; i < _tiposExcluidos.Length && n is not null; i++)
                    command.Parameters.Add(new SqlParameter("@t" + i, SqlDbType.NVarChar, 100) { Value = _tiposExcluidos[i] });

                using var r = await command.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var id = r.GetInt32(0);
                    if (productos.Count > 0 && productos[^1].Id == id) continue;
                    productos.Add((id, SqlPlumbing.GetStringOrNull(r, 1), Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
                        Dec(r, 3), Dec(r, 4), Dec(r, 5), r.IsDBNull(6) ? null : r.GetDateTime(6),
                        SqlPlumbing.GetStringOrNull(r, 7), SqlPlumbing.GetStringOrNull(r, 8), SqlPlumbing.GetStringOrNull(r, 9),
                        SqlPlumbing.GetStringOrNull(r, 10), SqlPlumbing.GetStringOrNull(r, 11), SqlPlumbing.GetStringOrNull(r, 12),
                        SqlPlumbing.GetStringOrNull(r, 13), SqlPlumbing.GetStringOrNull(r, 14)));
                }
            }
            if (productos.Count == 0) return new List<AwProductoOrigenFila>();

            // Consulta 2: capas de nivel 1 del rango; se descartan en C# los productos fuera de la página.
            var ids = productos.Select(p => p.Id).ToHashSet();
            var capas = new Dictionary<int, List<decimal?>>();
            using (var command = SqlPlumbing.CrearCommand(connection, SqlComposicion, _plomeria))
            {
                command.Parameters.Add(new SqlParameter("@min", SqlDbType.Int) { Value = productos[0].Id });
                command.Parameters.Add(new SqlParameter("@max", SqlDbType.Int) { Value = productos[^1].Id });
                using var r = await command.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var producto = r.GetInt32(0);
                    if (!ids.Contains(producto)) continue;
                    if (!capas.TryGetValue(producto, out var lista)) capas[producto] = lista = [];
                    lista.Add(Dec(r, 2));
                }
            }

            // Consulta 3: medidas de hoja entera del rango; mismo filtrado en C#.
            var medidas = new Dictionary<int, List<(decimal? Ancho, decimal? Alto)>>();
            using (var command = SqlPlumbing.CrearCommand(connection, SqlMedidas, _plomeria))
            {
                command.Parameters.Add(new SqlParameter("@min", SqlDbType.Int) { Value = productos[0].Id });
                command.Parameters.Add(new SqlParameter("@max", SqlDbType.Int) { Value = productos[^1].Id });
                using var r = await command.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var producto = r.GetInt32(0);
                    if (!ids.Contains(producto)) continue;
                    if (!medidas.TryGetValue(producto, out var lista)) medidas[producto] = lista = [];
                    lista.Add((Dec(r, 1), Dec(r, 2)));
                }
            }

            // Consulta 4: árbol de piezas del rango; mismo filtrado en C#.
            var piezas = new Dictionary<int, List<PiezaCruda>>();
            using (var command = SqlPlumbing.CrearCommand(connection, SqlComponentes, _plomeria))
            {
                command.Parameters.Add(new SqlParameter("@min", SqlDbType.Int) { Value = productos[0].Id });
                command.Parameters.Add(new SqlParameter("@max", SqlDbType.Int) { Value = productos[^1].Id });
                using var r = await command.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var producto = r.GetInt32(0);
                    if (!ids.Contains(producto)) continue;
                    if (!piezas.TryGetValue(producto, out var lista)) piezas[producto] = lista = [];
                    lista.Add(new(Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                        Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
                        r.GetInt32(3).ToString(CultureInfo.InvariantCulture), Dec(r, 4), SqlPlumbing.GetStringOrNull(r, 5),
                        SqlPlumbing.GetStringOrNull(r, 6), SqlPlumbing.GetStringOrNull(r, 7), SqlPlumbing.GetStringOrNull(r, 8),
                        SqlPlumbing.GetStringOrNull(r, 9), SqlPlumbing.GetStringOrNull(r, 10)));
                }
            }

            return productos.Select(p => ConstruirFila(p.Id, p.Mcode, p.Bloqueo, p.Dicke, p.Hoehe, p.Breite, p.Tt,
                p.B1, p.B2, p.B3, p.Unidad, capas.GetValueOrDefault(p.Id) ?? [], medidas.GetValueOrDefault(p.Id) ?? [],
                p.Grupo, p.Tipo, piezas.GetValueOrDefault(p.Id) ?? [], p.Wgr, p.WgrBez)).ToList();
        }, ct);

    /// <summary>Mapeo puro fila A+W → fila de origen (doc A+W §8.1). 0 en medidas = sin dato → null.</summary>
    internal static AwProductoOrigenFila ConstruirFila(
        int id, string? mcode, int kzGesperrt, decimal? espesor, decimal? alto, decimal? ancho, DateTime? transactionTime,
        string? bez1, string? bez2, string? bez3, string? unidad,
        IEnumerable<decimal?> capas, IEnumerable<(decimal? Ancho, decimal? Alto)> medidas,
        string? grupo = null, string? tipo = null, IEnumerable<PiezaCruda>? piezas = null,
        string? wgr = null, string? wgrDescripcion = null)
    {
        var descripcion = string.Join(' ', new[] { bez1, bez2, bez3 }
            .Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)));
        if (descripcion.Length == 0) descripcion = mcode?.Trim() ?? "";

        var esp = Positivo(espesor);
        var alt = Positivo(alto);
        var anc = Positivo(ancho);
        var composicion = ComponerComposicion(capas);
        // Hoja entera: una variante por medida de BA_LAGMA (ancho y alto > 0, sin duplicados).
        var porMedida = medidas.Where(m => m.Ancho > 0 && m.Alto > 0).Distinct().ToList();
        IReadOnlyList<AwProductoOrigenVariante> variantes =
            porMedida.Count > 0
                ? porMedida.Select(m => new AwProductoOrigenVariante(
                    string.Create(CultureInfo.InvariantCulture, $"{m.Ancho:0.####}x{m.Alto:0.####}"),
                    m.Alto, m.Ancho, esp, composicion)).ToList()
            : esp is null && alt is null && anc is null && composicion is null
                ? []
                : [new("BASE", alt, anc, esp, composicion)];

        return new(id.ToString(CultureInfo.InvariantCulture), descripcion.Length == 0 ? null : descripcion,
            unidad?.Trim(), kzGesperrt != 0, variantes, transactionTime,
            Vacio(mcode), Vacio(grupo), Vacio(tipo), ConstruirComponentes(piezas ?? []), Vacio(wgr), Vacio(wgrDescripcion));
    }

    /// <summary>Fila de <c>BA_STUKL</c> en orden <c>BOM_ID</c>, ya con los datos del componente.</summary>
    internal readonly record struct PiezaCruda(
        int Nivel, int Node, string Ref, decimal? Dicke, string? Tipo, string? Mcode, string? Bez1, string? Bez2, string? Bez3, string? Texto = null);
    // Texto = BA_STUKL_BEARB.BEA_TEXT: la plantilla de BA_BEZ3 ("FM [<§>] <%> ...") ya resuelta por A+W para esa línea.

    /// <summary>
    /// Árbol por posición: Orden = posición 1-based; PadreOrden = BOM_NODE (0 = raíz → null). Si el nodo no apunta a una
    /// fila anterior de nivel padre (regla no cumplida) la fila queda sin padre en vez de inventar uno.
    /// </summary>
    internal static List<AwProductoOrigenComponente> ConstruirComponentes(IEnumerable<PiezaCruda> piezas)
    {
        var lista = piezas.ToList();
        var resultado = new List<AwProductoOrigenComponente>(lista.Count);
        for (var i = 0; i < lista.Count; i++)
        {
            var p = lista[i];
            int? padre = p.Node >= 1 && p.Node <= i && lista[p.Node - 1].Nivel == p.Nivel - 1 ? p.Node : null;
            var descripcion = string.Join(' ', new[] { p.Bez1, p.Bez2, string.IsNullOrWhiteSpace(p.Texto) ? p.Bez3 : p.Texto }
                .Select(x => x == null ? null : Regex.Replace(x.Trim(), @"\s{2,}", " ")).Where(x => !string.IsNullOrEmpty(x)));
            if (descripcion.Length == 0) descripcion = p.Mcode?.Trim() ?? "";
            resultado.Add(new(i + 1, Math.Max(p.Nivel, 1), padre, p.Ref, descripcion.Length == 0 ? null : descripcion,
                Vacio(p.Tipo), Positivo(p.Dicke)));
        }
        return resultado;
    }

    private static string? Vacio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Espesores de las capas de nivel 1, en orden, unidos con '+'. Capas sin espesor (&lt;= 0: procesos, kits) no cuentan.</summary>
    internal static string? ComponerComposicion(IEnumerable<decimal?> capas)
    {
        var texto = string.Join('+', capas.Where(e => e > 0).Select(e => e!.Value.ToString("0.############", CultureInfo.InvariantCulture)));
        return texto.Length == 0 ? null : texto;
    }

    private static decimal? Positivo(decimal? v) => v > 0 ? v : null;

    private static decimal? Dec(DbDataReader r, int i) =>
        r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);
}
