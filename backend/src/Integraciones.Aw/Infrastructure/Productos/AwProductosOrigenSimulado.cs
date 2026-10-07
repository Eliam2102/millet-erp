using System.Globalization;
using System.Text.Json;
using Millet.Integraciones.Aw.Application.Productos;

namespace Millet.Integraciones.Aw.Infrastructure.Productos;

/// <summary>
/// Fuente en memoria para operar/probar sin A+W. Carga el formato de los fixtures
/// <c>{origen:{vw_erp_articulo:[...]}}</c>. Cursor = índice (orden estable por producto_ref;
/// admite duplicados de referencia).
/// </summary>
public sealed class AwProductosOrigenSimulado : IAwProductosOrigen
{
    private readonly List<AwProductoOrigenFila> _filas;

    public AwProductosOrigenSimulado(IEnumerable<AwProductoOrigenFila>? filas = null) =>
        _filas = (filas ?? []).OrderBy(f => f.ProductoRef, StringComparer.Ordinal).ToList();

    public static AwProductosOrigenSimulado DesdeArchivo(string ruta) => DesdeJson(File.ReadAllText(ruta));

    public static AwProductosOrigenSimulado DesdeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var filas = doc.RootElement.GetProperty("origen").GetProperty("vw_erp_articulo").EnumerateArray()
            .Select(e => new AwProductoOrigenFila(
                Str(e, "producto_ref"), Str(e, "descripcion"), Str(e, "unidad_medida"),
                e.TryGetProperty("baja", out var b) && b.ValueKind == JsonValueKind.True,
                e.GetProperty("variantes").EnumerateArray().Select(v => new AwProductoOrigenVariante(
                    Str(v, "clave_variante"), Dec(v, "alto_mm"), Dec(v, "ancho_mm"),
                    Dec(v, "espesor_mm"), Str(v, "composicion"))).ToList(),
                Str(e, "TRANSACTION_TIME") is { } t ? DateTime.Parse(t, CultureInfo.InvariantCulture) : null,
                Str(e, "codigo_modelo"), Str(e, "grupo"), Str(e, "tipo"),
                e.TryGetProperty("componentes", out var cs) && cs.ValueKind == JsonValueKind.Array
                    ? cs.EnumerateArray().Select(c => new AwProductoOrigenComponente(
                        int.Parse(Str(c, "orden")!, CultureInfo.InvariantCulture), int.Parse(Str(c, "nivel")!, CultureInfo.InvariantCulture),
                        Str(c, "padre_orden") is { } po ? int.Parse(po, CultureInfo.InvariantCulture) : null,
                        Str(c, "ref"), Str(c, "descripcion"), Str(c, "tipo"), Dec(c, "espesor_mm"))).ToList()
                    : null,
                Str(e, "wgr"), Str(e, "wgr_descripcion")))
            .ToList();
        return new(filas);
    }

    public Task<AwProductoOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) =>
        Task.FromResult(_filas.FirstOrDefault(f => f.ProductoRef == referencia));

    public Task<AwProductosPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
    {
        tamano = Math.Clamp(tamano, 1, AwProductosOptions.TamanoLoteMaximo);
        var desde = 0;
        if (cursor is not null && !int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out desde))
            throw new ArgumentException("Cursor inválido.", nameof(cursor));

        var filas = _filas.Skip(desde).Take(tamano).ToList();
        var siguiente = desde + filas.Count < _filas.Count
            ? (desde + filas.Count).ToString(CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new AwProductosPagina(filas, siguiente));
    }

    private static string? Str(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;

    private static decimal? Dec(JsonElement e, string n) =>
        Str(e, n) is { } s ? decimal.Parse(s, CultureInfo.InvariantCulture) : null;
}
