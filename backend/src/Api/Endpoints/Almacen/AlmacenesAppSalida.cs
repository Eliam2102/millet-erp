using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// AJUSTE_SALIDA, ROTURA y SALIDA_MANUAL de la app de Almacenes (F1-MP-07). Lee <c>datos</c> según el contrato v6
/// §7.1, §8.3 y §8.5: sucursal, movimiento, motivo/origen/mesa/jobid/pedidoAw y <c>almacenes[].grupos[]</c>
/// (articuloAw, ancho/alto, piezas, costo). Se aplanan los grupos de todos los almacenes (S5: el almacén general
/// del mensaje se ignora; el ERP decide el sub-almacén) y se resuelven con una sola llamada al puerto; la salida
/// es todo o nada (S2). <c>costo</c> por grupo (solo SALIDA_MANUAL) es el total en MXN del grupo; el unitario es
/// costo/piezas y, si falta, se usa el costo promedio (S4). Artículo, medida o sucursal inexistente → ERROR sin
/// mover nada. Sin existencia → se acepta y queda en revisión.
/// </summary>
public static class AlmacenesAppSalida
{
    public static readonly IReadOnlyDictionary<string, TipoMovimiento> Tipos = new Dictionary<string, TipoMovimiento>
    {
        // Supuesto por validar con Jorge (D1): AJUSTE_SALIDA = consumo del corte (contrato §tabla de mensajes).
        ["AJUSTE_SALIDA"] = TipoMovimiento.SalidaConsumo,
        ["ROTURA"] = TipoMovimiento.BajaPorDano,
        ["SALIDA_MANUAL"] = TipoMovimiento.AjusteNegativo,
    };

    public static async Task<(string Estado, string? Folio, string? Error)> ManejarAsync(
        string tipo, JsonElement datos, AlmacenDbContext db, IAppSalidaResolverPort resolver,
        IUsuarioServicioReadPort usuarioServicio, CancellationToken ct)
    {
        if (datos.ValueKind != JsonValueKind.Object) return ("ERROR", null, "datos debe ser un objeto.");
        var sucursal = Texto(datos, "sucursal");
        if (string.IsNullOrWhiteSpace(sucursal)) return ("ERROR", null, "datos.sucursal es requerida.");
        if (!datos.TryGetProperty("almacenes", out var almacenes) || almacenes.ValueKind != JsonValueKind.Array
            || almacenes.GetArrayLength() == 0)
            return ("ERROR", null, "almacenes debe ser un arreglo con al menos un elemento.");

        var items = new List<(string Articulo, decimal? Ancho, decimal? Alto, decimal Piezas, decimal? CostoTotal)>();
        var i = 0;
        foreach (var alm in almacenes.EnumerateArray())
        {
            if (alm.ValueKind != JsonValueKind.Object) return ("ERROR", null, $"almacenes[{i}] debe ser un objeto.");
            if (!alm.TryGetProperty("grupos", out var grupos) || grupos.ValueKind != JsonValueKind.Array
                || grupos.GetArrayLength() == 0)
                return ("ERROR", null, $"almacenes[{i}].grupos debe ser un arreglo con al menos un elemento.");
            var j = 0;
            foreach (var g in grupos.EnumerateArray())
            {
                var p = $"almacenes[{i}].grupos[{j}]";
                if (g.ValueKind != JsonValueKind.Object) return ("ERROR", null, $"{p} debe ser un objeto.");
                string? articulo = null;
                if (g.TryGetProperty("articuloAw", out var a) && a.ValueKind != JsonValueKind.Null)
                {
                    if (a.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
                        return ("ERROR", null, $"{p}.articuloAw debe ser numérico o texto.");
                    articulo = a.ValueKind == JsonValueKind.Number ? a.GetRawText() : a.GetString();
                }
                if (string.IsNullOrWhiteSpace(articulo)) return ("ERROR", null, $"{p}.articuloAw es requerido.");
                if (!TryNumero(g, "piezas", out var piezas)) return ("ERROR", null, $"{p}.piezas debe ser numérico.");
                if (piezas is null or <= 0) return ("ERROR", null, $"{p}.piezas debe ser mayor que cero.");
                if (!TryNumero(g, "ancho", out var ancho)) return ("ERROR", null, $"{p}.ancho debe ser numérico.");
                if (!TryNumero(g, "alto", out var alto)) return ("ERROR", null, $"{p}.alto debe ser numérico.");
                if (ancho is <= 0 || alto is <= 0 || (ancho is null) != (alto is null))
                    return ("ERROR", null, $"{p}.ancho y {p}.alto deben venir juntos y ser mayores que cero, o no venir.");
                // v6 §7.1: el costo solo viaja en SALIDA_MANUAL; en AJUSTE_SALIDA y ROTURA se ignora (costo promedio).
                decimal? costo = null;
                if (tipo == "SALIDA_MANUAL")
                {
                    if (!TryNumero(g, "costo", out costo)) return ("ERROR", null, $"{p}.costo debe ser numérico.");
                    if (costo is < 0) return ("ERROR", null, $"{p}.costo no puede ser negativo.");
                }
                items.Add((articulo, ancho, alto, piezas.Value, costo));
                j++;
            }
            i++;
        }

        var sp = await usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo) return ("ERROR", null, "El usuario de servicio del ERP no está disponible.");

        var resuelto = await resolver.ResolverAsync(sucursal,
            items.Select(x => new GrupoSalidaApp(x.Articulo, x.Ancho, x.Alto)).ToList(), ct);
        if (resuelto.Error is not null) return ("ERROR", null, resuelto.Error);

        var motivo = $"App Almacenes {tipo} {Texto(datos, "movimiento")}".TrimEnd();
        var detalle = tipo == "AJUSTE_SALIDA"
            ? string.Concat(
                Texto(datos, "mesa") is { Length: > 0 } mesa ? $" mesa {mesa}" : "",
                Texto(datos, "jobid") is { Length: > 0 } job ? $" job {job}" : "",
                Texto(datos, "pedidoAw") is { Length: > 0 } ped ? $" pedido {ped}" : "")
            : Texto(datos, "motivo") is { Length: > 0 } mot ? $": {mot.Trim()}" : "";
        motivo += detalle;
        if (motivo.Length > 500) motivo = motivo[..500];

        var salidas = items.Select((x, k) =>
        {
            var r = resuelto.Grupos[k];
            // ponytail: redondeo a la precisión de la columna (14,4); el residuo no se redistribuye entre piezas.
            var costoUnit = x.CostoTotal is { } ct2 ? Math.Round(ct2 / x.Piezas, 4) : (decimal?)null;
            return new SalidaApp(Tipos[tipo], resuelto.SucursalId, r.ArticuloId, r.UnidadMedida, r.ProductoAwVarianteId,
                x.Piezas, costoUnit, motivo, r.AnchoMm, r.AltoMm);
        }).ToList();
        return await SalidaAppAlmacenes.RegistrarAsync(db, sp.EmpresaId, sp.Id, salidas, ct);
    }

    private static string? Texto(JsonElement o, string n) =>
        o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Ausente o null → true con null; presente con tipo distinto de número → false.
    private static bool TryNumero(JsonElement o, string n, out decimal? valor)
    {
        valor = null;
        if (!o.TryGetProperty(n, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var d)) return false;
        valor = d;
        return true;
    }
}
