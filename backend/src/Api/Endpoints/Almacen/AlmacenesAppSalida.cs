using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// AJUSTE_SALIDA, ROTURA y SALIDA_MANUAL de la app de Almacenes (F1-MP-07). Lee <c>datos</c> según el contrato v6
/// §7.1, §8.3 y §8.5 con las correcciones de TI del 5-nov: sucursal, movimiento, motivo/origen/mesa/jobid/pedidoAw,
/// <c>documentoPrevio</c> (solo AJUSTE_SALIDA, opcional) y <c>almacenes[].grupos[]</c> (articuloAw, ancho/alto en mm
/// sin redondear, piezas, costo). Se aplanan los grupos de todos los almacenes (el almacén general y su id se
/// ignoran: el ERP decide el sub-almacén y no se amarra a ids) y se resuelven con una sola llamada al puerto; la
/// salida es todo o nada. El costo (solo SALIDA_MANUAL, cualquiera que sea su origen: captura, cancelación de
/// entrada o devolución de venta) es el promedio de A+W al momento del movimiento: <c>costoM2</c> en vidrio o
/// <c>costoPieza</c> en material por cantidad; se guarda tal cual llega y nunca se recalcula. Costo de la hoja =
/// costoM2 × m² de la hoja; del grupo = costoM2 × m2 del grupo. Ya no existe el costo total del grupo ni
/// <c>origenCosto</c>. Con medida (vidrio) <c>piezas</c> son hojas enteras y el saldo vive en m²: se baja
/// piezas × m² de la hoja (el <c>m2</c> del grupo, si viene, debe coincidir). Sin medida, <c>piezas</c> es el conteo
/// de unidades. Artículo, medida o sucursal inexistente → ERROR sin mover nada. Sin existencia → ERROR con folio de
/// revisión y el mensaje queda marcado (el llamador lo acusa).
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

    /// <summary>Salidas ya validadas y resueltas, listas para registrarse con empresa y usuario de servicio explícitos.</summary>
    public sealed record Resuelta(Guid EmpresaId, Guid UsuarioId, IReadOnlyList<SalidaApp> Salidas);

    /// <summary>Valida y resuelve; no escribe. Devuelve la salida resuelta o el texto del ERROR.</summary>
    public static async Task<(Resuelta? Salida, string? Error)> ManejarAsync(
        string tipo, JsonElement datos, IAppSalidaResolverPort resolver,
        IUsuarioServicioReadPort usuarioServicio, CancellationToken ct)
    {
        if (datos.ValueKind != JsonValueKind.Object) return (null, "datos debe ser un objeto.");
        var sucursal = Texto(datos, "sucursal");
        if (string.IsNullOrWhiteSpace(sucursal)) return (null, "datos.sucursal es requerida.");
        if (!datos.TryGetProperty("almacenes", out var almacenes) || almacenes.ValueKind != JsonValueKind.Array
            || almacenes.GetArrayLength() == 0)
            return (null, "almacenes debe ser un arreglo con al menos un elemento.");

        var items = new List<(string Articulo, decimal? Ancho, decimal? Alto, decimal Piezas, decimal? CostoUnitario, decimal? M2, string Ruta)>();
        var i = 0;
        foreach (var alm in almacenes.EnumerateArray())
        {
            if (alm.ValueKind != JsonValueKind.Object) return (null, $"almacenes[{i}] debe ser un objeto.");
            if (!alm.TryGetProperty("grupos", out var grupos) || grupos.ValueKind != JsonValueKind.Array
                || grupos.GetArrayLength() == 0)
                return (null, $"almacenes[{i}].grupos debe ser un arreglo con al menos un elemento.");
            var j = 0;
            foreach (var g in grupos.EnumerateArray())
            {
                var p = $"almacenes[{i}].grupos[{j}]";
                if (g.ValueKind != JsonValueKind.Object) return (null, $"{p} debe ser un objeto.");
                string? articulo = null;
                if (g.TryGetProperty("articuloAw", out var a) && a.ValueKind != JsonValueKind.Null)
                {
                    if (a.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
                        return (null, $"{p}.articuloAw debe ser numérico o texto.");
                    articulo = a.ValueKind == JsonValueKind.Number ? a.GetRawText() : a.GetString();
                }
                if (string.IsNullOrWhiteSpace(articulo)) return (null, $"{p}.articuloAw es requerido.");
                if (!TryNumero(g, "piezas", out var piezas)) return (null, $"{p}.piezas debe ser numérico.");
                if (piezas is null or <= 0) return (null, $"{p}.piezas debe ser mayor que cero.");
                if (!TryNumero(g, "ancho", out var ancho)) return (null, $"{p}.ancho debe ser numérico.");
                if (!TryNumero(g, "alto", out var alto)) return (null, $"{p}.alto debe ser numérico.");
                if (ancho is <= 0 || alto is <= 0 || (ancho is null) != (alto is null))
                    return (null, $"{p}.ancho y {p}.alto deben venir juntos y ser mayores que cero, o no venir.");
                if (!TryNumero(g, "m2", out var m2)) return (null, $"{p}.m2 debe ser numérico.");
                if (m2 is <= 0) return (null, $"{p}.m2 debe ser mayor que cero.");
                // Solo SALIDA_MANUAL trae costo (5-nov): promedio de A+W por m² (vidrio) o por pieza (por cantidad).
                decimal? costo = null;
                if (tipo == "SALIDA_MANUAL")
                {
                    if (!TryNumero(g, "costoM2", out var costoM2)) return (null, $"{p}.costoM2 debe ser numérico.");
                    if (!TryNumero(g, "costoPieza", out var costoPieza)) return (null, $"{p}.costoPieza debe ser numérico.");
                    if (costoM2 is < 0 || costoPieza is < 0) return (null, $"{p}.costoM2 y {p}.costoPieza no pueden ser negativos.");
                    var conMedida = ancho is not null;
                    if (conMedida && costoM2 is null && costoPieza is not null) return (null, $"{p} lleva medida: el costo va en costoM2.");
                    if (!conMedida && costoPieza is null && costoM2 is not null) return (null, $"{p} no lleva medida: el costo va en costoPieza.");
                    costo = conMedida ? costoM2 : costoPieza;
                }
                items.Add((articulo, ancho, alto, piezas.Value, costo, m2, p));
                j++;
            }
            i++;
        }

        var sp = await usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo) return (null, "El usuario de servicio del ERP no está disponible.");

        var resuelto = await resolver.ResolverAsync(sucursal,
            items.Select(x => new GrupoSalidaApp(x.Articulo, x.Ancho, x.Alto)).ToList(), ct);
        if (resuelto.Error is not null) return (null, resuelto.Error);

        var motivo = $"App Almacenes {tipo} {Texto(datos, "movimiento")}".TrimEnd();
        var detalle = tipo == "AJUSTE_SALIDA"
            ? string.Concat(
                // El descuento llega al validar el documento previo (no al cortar); el ERP no reserva y solo lo deja en el motivo.
                DocumentoPrevio(datos) is { Length: > 0 } dp ? $" documento previo {dp}" : "",
                Texto(datos, "mesa") is { Length: > 0 } mesa ? $" mesa {mesa}" : "",
                Texto(datos, "jobid") is { Length: > 0 } job ? $" job {job}" : "",
                Texto(datos, "pedidoAw") is { Length: > 0 } ped ? $" pedido {ped}" : "")
            : Texto(datos, "motivo") is { Length: > 0 } mot ? $": {mot.Trim()}" : "";
        motivo += detalle;
        if (motivo.Length > 500) motivo = motivo[..500];

        var salidas = new List<SalidaApp>(items.Count);
        for (var k = 0; k < items.Count; k++)
        {
            var x = items[k];
            var r = resuelto.Grupos[k];
            // Con medida el saldo está en m²: piezas son hojas enteras y la cantidad es hojas × m² de la hoja.
            var cantidad = x.Piezas * (r.AreaHojaM2 ?? 1m);
            // El área de la hoja se redondea a 4 decimales; el m2 del grupo puede venir sin redondear, así que la tolerancia crece con las hojas.
            if (r.AreaHojaM2 is not null && x.M2 is { } m2 && Math.Abs(m2 - cantidad) > 0.0001m + x.Piezas * 0.00005m)
                return (null, $"{x.Ruta}.m2 ({m2:0.####}) no coincide con piezas × área de la hoja ({cantidad:0.####}).");
            // El valor que llega se guarda tal cual (a la precisión de la columna, 14,4); no se recalcula contra A+W.
            var costoUnit = x.CostoUnitario is { } cu ? Math.Round(cu, 4) : (decimal?)null;
            salidas.Add(new SalidaApp(Tipos[tipo], resuelto.SucursalId, r.ArticuloId, r.UnidadMedida, r.ProductoAwVarianteId,
                cantidad, costoUnit, motivo, r.AnchoMm, r.AltoMm));
        }
        return (new Resuelta(sp.EmpresaId, sp.Id, salidas), null);
    }

    // documentoPrevio: el contrato aún no fija su forma; solo se toma si viene como texto o número (otra forma se ignora, sin inventar campos).
    private static string? DocumentoPrevio(JsonElement datos)
    {
        if (!datos.TryGetProperty("documentoPrevio", out var d)) return null;
        var t = d.ValueKind switch { JsonValueKind.String => d.GetString(), JsonValueKind.Number => d.GetRawText(), _ => null };
        return t?.Trim() is { Length: > 60 } largo ? largo[..60] : t?.Trim();
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
