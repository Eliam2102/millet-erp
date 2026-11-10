using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Ports;
using static Millet.Api.Endpoints.Almacen.AlmacenesAppSalida;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// ENTRADA_ALMACEN de la app de Almacenes (M-159, contrato v8 §1): <c>entrada</c>, <c>compra</c> (folio de la OC del ERP),
/// <c>sucursal</c>, <c>piezas</c> (total) y <c>lineas[]</c> con su detalle por división + línea en <c>almacenes[].grupos[]</c>
/// (si una línea no trae <c>almacenes</c>, se toman sus <c>grupos</c>). La entrada trae el TOTAL de la compra, incluidas las
/// <c>reclamadas</c>, que salen aparte por RECLAMACION_PROVEEDOR: aquí entra el total. La suma de los grupos debe dar las piezas
/// de la línea y la de las líneas el total. Artículo, medida o sucursal inexistente → ERROR sin mover nada. Con medida,
/// <c>piezas</c> son hojas enteras y el saldo sube piezas × m² de la hoja (el <c>m2</c> del grupo, si viene, debe coincidir).
/// </summary>
public static class AlmacenesAppEntrada
{
    public const string Tipo = "ENTRADA_ALMACEN";
    private const int MaxLineas = 500;

    public sealed record Resuelta(Guid EmpresaId, Guid UsuarioId, Guid SucursalId, string Motivo, IReadOnlyList<EntradaApp> Entradas,
        string? Compra = null, string? Entrada = null);

    /// <summary>Valida y resuelve; no escribe. Devuelve la entrada resuelta o el texto del ERROR.</summary>
    public static async Task<(Resuelta? Entrada, string? Error)> ManejarAsync(
        JsonElement datos, IAppSalidaResolverPort resolver, IUsuarioServicioReadPort usuarioServicio, CancellationToken ct)
    {
        if (datos.ValueKind != JsonValueKind.Object) return (null, "datos debe ser un objeto.");
        var sucursal = Texto(datos, "sucursal");
        if (string.IsNullOrWhiteSpace(sucursal)) return (null, "datos.sucursal es requerida.");
        if (!datos.TryGetProperty("lineas", out var lineas) || lineas.ValueKind != JsonValueKind.Array
            || lineas.GetArrayLength() == 0 || lineas.GetArrayLength() > MaxLineas)
            return (null, $"datos.lineas debe traer de 1 a {MaxLineas} líneas.");

        var items = new List<(string Articulo, decimal? Ancho, decimal? Alto, decimal Piezas, decimal? M2, string Ruta)>();
        decimal total = 0;
        var i = 0;
        foreach (var l in lineas.EnumerateArray())
        {
            var pl = $"lineas[{i}]";
            if (l.ValueKind != JsonValueKind.Object) return (null, $"{pl} debe ser un objeto.");
            if (!TryNumero(l, "piezas", out var piezasLinea) || piezasLinea is null or <= 0)
                return (null, $"{pl}.piezas debe ser mayor que cero.");
            if (!TryNumero(l, "reclamadas", out var reclamadas) || reclamadas is < 0 || reclamadas > piezasLinea)
                return (null, $"{pl}.reclamadas debe estar entre cero y las piezas de la línea.");
            var articuloLinea = ArticuloAw(l, out _);

            var grupos = new List<(JsonElement G, string Ruta)>();
            if (l.TryGetProperty("almacenes", out var almacenes) && almacenes.ValueKind == JsonValueKind.Array && almacenes.GetArrayLength() > 0)
            {
                var k = 0;
                foreach (var alm in almacenes.EnumerateArray())
                {
                    if (alm.ValueKind != JsonValueKind.Object || !alm.TryGetProperty("grupos", out var gs) || gs.ValueKind != JsonValueKind.Array)
                        return (null, $"{pl}.almacenes[{k}].grupos debe ser un arreglo.");
                    var j = 0;
                    foreach (var g in gs.EnumerateArray()) grupos.Add((g, $"{pl}.almacenes[{k}].grupos[{j++}]"));
                    k++;
                }
            }
            else if (l.TryGetProperty("grupos", out var gs) && gs.ValueKind == JsonValueKind.Array)
            {
                var j = 0;
                foreach (var g in gs.EnumerateArray()) grupos.Add((g, $"{pl}.grupos[{j++}]"));
            }
            if (grupos.Count == 0) return (null, $"{pl} debe traer al menos un grupo (almacenes[].grupos o grupos).");

            decimal sumaLinea = 0;
            foreach (var (g, p) in grupos)
            {
                if (g.ValueKind != JsonValueKind.Object) return (null, $"{p} debe ser un objeto.");
                var articulo = ArticuloAw(g, out var malTipo) ?? articuloLinea;
                if (malTipo) return (null, $"{p}.articuloAw debe ser numérico o texto.");
                if (string.IsNullOrWhiteSpace(articulo)) return (null, $"{p}.articuloAw es requerido.");
                if (!TryNumero(g, "piezas", out var piezas) || piezas is null or <= 0) return (null, $"{p}.piezas debe ser mayor que cero.");
                if (!TryNumero(g, "ancho", out var ancho) || !TryNumero(g, "alto", out var alto)
                    || ancho is <= 0 || alto is <= 0 || (ancho is null) != (alto is null))
                    return (null, $"{p}.ancho y {p}.alto deben venir juntos y ser mayores que cero, o no venir.");
                if (!TryNumero(g, "m2", out var m2) || m2 is <= 0) return (null, $"{p}.m2 debe ser numérico y mayor que cero.");
                items.Add((articulo.Trim(), ancho, alto, piezas.Value, m2, p));
                sumaLinea += piezas.Value;
            }
            if (sumaLinea != piezasLinea)
                return (null, $"{pl}: la suma de piezas de sus grupos ({sumaLinea:0.####}) no coincide con piezas ({piezasLinea:0.####}).");
            total += piezasLinea.Value;
            i++;
        }
        if (!TryNumero(datos, "piezas", out var piezasTotal)) return (null, "datos.piezas debe ser numérico.");
        if (piezasTotal is not null && piezasTotal != total)
            return (null, $"datos.piezas ({piezasTotal:0.####}) no coincide con la suma de las líneas ({total:0.####}).");

        var sp = await usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo) return (null, "El usuario de servicio del ERP no está disponible.");

        var resuelto = await resolver.ResolverAsync(sucursal,
            items.Select(x => new GrupoSalidaApp(x.Articulo, x.Ancho, x.Alto)).ToList(), ct);
        if (resuelto.Error is not null) return (null, resuelto.Error);

        var entradas = new List<EntradaApp>(items.Count);
        for (var k = 0; k < items.Count; k++)
        {
            var x = items[k];
            var r = resuelto.Grupos[k];
            var cantidad = x.Piezas * (r.AreaHojaM2 ?? 1m);
            if (r.AreaHojaM2 is not null && x.M2 is { } m2 && Math.Abs(m2 - cantidad) > 0.0001m + x.Piezas * 0.00005m)
                return (null, $"{x.Ruta}.m2 ({m2:0.####}) no coincide con piezas × área de la hoja ({cantidad:0.####}).");
            entradas.Add(new EntradaApp(r.ArticuloId, r.UnidadMedida, r.ProductoAwVarianteId, cantidad, r.AnchoMm, r.AltoMm));
        }

        var entrada = Texto(datos, "entrada")?.Trim() is { Length: > 0 } en ? en : null;
        var compra = Texto(datos, "compra")?.Trim() is { Length: > 0 } oc ? oc : null;
        var motivo = string.Concat($"App Almacenes {Tipo}", entrada is null ? "" : $" {entrada}", compra is null ? "" : $" compra {compra}");
        if (motivo.Length > 500) motivo = motivo[..500];
        return (new Resuelta(sp.EmpresaId, sp.Id, resuelto.SucursalId, motivo, entradas, compra, entrada), null);
    }

    // articuloAw: número o texto; ausente/null → null. Otro tipo → malTipo.
    private static string? ArticuloAw(JsonElement o, out bool malTipo)
    {
        malTipo = false;
        if (!o.TryGetProperty("articuloAw", out var a) || a.ValueKind == JsonValueKind.Null) return null;
        if (a.ValueKind == JsonValueKind.Number) return a.GetRawText();
        if (a.ValueKind == JsonValueKind.String) return a.GetString();
        malTipo = true;
        return null;
    }
}
