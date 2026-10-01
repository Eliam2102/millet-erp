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
/// composición <c>BA_STUKL</c> nivel 1). SQL Server 2016: sin STRING_AGG/STRING_SPLIT/JSON, por eso la
/// composición se arma en C#. Deshabilitado por defecto (ver <see cref="ProductosDependencyInjection"/>).
/// </summary>
public sealed class AwProductosSqlOrigen : IAwProductosOrigen
{
    private const string Columnas = """
        p.BA_PRODUKT, p.BA_MCODE, p.KZ_GESPERRT, p.BA_MASS_DICKE, p.BA_STD_HOEHE, p.BA_STD_BREITE,
        p.TRANSACTION_TIME, b.BA_BEZ1, b.BA_BEZ2, b.BA_BEZ3, b.BA_MENGENEINH
        """;

    private const string ColumnasInternas =
        "BA_PRODUKT, BA_MCODE, KZ_GESPERRT, BA_MASS_DICKE, BA_STD_HOEHE, BA_STD_BREITE, TRANSACTION_TIME";

    internal const string MarcaTipos = "/*TIPOS*/";

    // El TOP va en la tabla derivada para que el lote cuente productos y no filas del join con BEZ.
    public const string SqlPagina =
        "SELECT " + Columnas + $"""

          FROM (SELECT TOP (@n) {ColumnasInternas}
                  FROM SYSADM.BA_PRODUKTE WHERE BA_PRODUKT > @cursor {MarcaTipos} ORDER BY BA_PRODUKT) AS p
          LEFT JOIN SYSADM.BA_PRODUKTE_BEZ AS b ON b.BA_PRODUKT = p.BA_PRODUKT AND b.SPRACH_ID = 0
         ORDER BY p.BA_PRODUKT
        """;

    public const string SqlReferencia =
        "SELECT " + Columnas + $"""

          FROM (SELECT TOP (1) {ColumnasInternas}
                  FROM SYSADM.BA_PRODUKTE WHERE BA_PRODUKT = @cursor ORDER BY BA_PRODUKT) AS p
          LEFT JOIN SYSADM.BA_PRODUKTE_BEZ AS b ON b.BA_PRODUKT = p.BA_PRODUKT AND b.SPRACH_ID = 0
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
                DateTime? Tt, string? B1, string? B2, string? B3, string? Unidad)>();
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
                        SqlPlumbing.GetStringOrNull(r, 10)));
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

            return productos.Select(p => ConstruirFila(p.Id, p.Mcode, p.Bloqueo, p.Dicke, p.Hoehe, p.Breite, p.Tt,
                p.B1, p.B2, p.B3, p.Unidad, capas.GetValueOrDefault(p.Id) ?? [])).ToList();
        }, ct);

    /// <summary>Mapeo puro fila A+W → fila de origen (doc A+W §8.1). 0 en medidas = sin dato → null.</summary>
    internal static AwProductoOrigenFila ConstruirFila(
        int id, string? mcode, int kzGesperrt, decimal? espesor, decimal? alto, decimal? ancho, DateTime? transactionTime,
        string? bez1, string? bez2, string? bez3, string? unidad,
        IEnumerable<decimal?> capas)
    {
        var descripcion = string.Join(' ', new[] { bez1, bez2, bez3 }
            .Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)));
        if (descripcion.Length == 0) descripcion = mcode?.Trim() ?? "";

        var esp = Positivo(espesor);
        var alt = Positivo(alto);
        var anc = Positivo(ancho);
        var composicion = ComponerComposicion(capas);
        IReadOnlyList<AwProductoOrigenVariante> variantes =
            esp is null && alt is null && anc is null && composicion is null
                ? []
                : [new("BASE", alt, anc, esp, composicion)];

        return new(id.ToString(CultureInfo.InvariantCulture), descripcion.Length == 0 ? null : descripcion,
            unidad?.Trim(), kzGesperrt != 0, variantes, transactionTime);
    }

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
