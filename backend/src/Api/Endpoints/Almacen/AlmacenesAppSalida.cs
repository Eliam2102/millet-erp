using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Contabilidad.Application.PublicPorts;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// AJUSTE_SALIDA, ROTURA y SALIDA_MANUAL de la app de Almacenes (F1-MP-07). Lee <c>datos</c> según el contrato v6
/// §7.1, §8.3 y §8.5 con las correcciones de TI del 5-nov: sucursal, movimiento, motivo/origen/mesa/jobid/pedidoAw,
/// <c>documentoPrevio</c> (solo AJUSTE_SALIDA, opcional) y <c>almacenes[].grupos[]</c> (articuloAw, ancho/alto en mm
/// sin redondear, piezas, costo). Se aplanan los grupos de todos los almacenes; cada grupo lleva la llave de su almacén
/// (<c>almacenGeneral.tipo</c> + <c>linea</c>, M-446) y su lote (se toma primero), y se resuelven con una sola llamada al puerto; la
/// salida es todo o nada. Costo (contrato v9, 10-nov; Fernando, 9-nov): toda salida, incluida SALIDA_MANUAL, sale al
/// costo promedio del ERP del saldo que toca. El <c>costoM2</c> (vidrio) o <c>costoPieza</c> (por cantidad) que trae
/// SALIDA_MANUAL es el promedio de A+W y para el ERP es informativo: se valida su forma pero no se usa. <c>moneda</c>, si
/// viene, debe ser PESOSMX (o MXN). <c>motivoClave</c> (v9) se guarda en el movimiento (<c>MotivoClave</c>) y en su motivo. Con medida (vidrio) <c>piezas</c> son hojas enteras y el saldo vive en m²: se baja
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
        // v9: lo que llegó roto o no llegó sale enseguida de la entrada (folio DEV); sin el flujo 8.B de CxP.
        [Reclamacion] = TipoMovimiento.SalidaPorDevolucionAProveedor,
    };

    /// <summary>
    /// ENTRADA_MANUAL (v9): misma forma que SALIDA_MANUAL; se parsea aquí y la registra <see cref="EntradaAppAlmacenes"/> como
    /// ajuste positivo al promedio del ERP. No está en <see cref="Tipos"/> porque no es salida.
    /// </summary>
    public const string EntradaManual = "ENTRADA_MANUAL";

    /// <summary>
    /// RECLAMACION_PROVEEDOR (v9): <c>lineas[]</c> con sus <c>grupos[]</c> (sin almacén: se reparte en la sucursal, primero el lote).
    /// El costo del mensaje es el de la compra en su moneda: no se usa (sale al costo de la entrada o al promedio).
    /// </summary>
    public const string Reclamacion = "RECLAMACION_PROVEEDOR";

    /// <summary>Salidas ya validadas y resueltas, listas para registrarse con empresa y usuario de servicio explícitos.</summary>
    public sealed record Resuelta(Guid EmpresaId, Guid UsuarioId, IReadOnlyList<SalidaApp> Salidas, OrigenApp? Origen = null);

    /// <summary>
    /// Valida y resuelve; no escribe. Devuelve la salida resuelta o el texto del ERROR.
    /// H1 (86e3z5w25): <c>datos.area</c> (clave del CeCo, Dim2) y <c>datos.equipo</c> (clave del equipo, Dim3), opcionales, valen para
    /// todo el movimiento. Se resuelven en la sucursal con <paramref name="dimensiones"/>; uno inválido es ERROR. Si la cuenta de la
    /// salida los exige (C-03) y no vienen, es ERROR solo con <paramref name="exigirAreaEquipo"/>; si no, la póliza queda en Rechazos.
    /// </summary>
    public static async Task<(Resuelta? Salida, string? Error)> ManejarAsync(
        string tipo, JsonElement datos, IAppSalidaResolverPort resolver,
        IUsuarioServicioReadPort usuarioServicio, CancellationToken ct,
        IDimensionesSalidaInventarioPort? dimensiones = null, bool exigirAreaEquipo = false,
        IReadOnlyDictionary<string, string>? centrosAlmacen = null)
    {
        if (datos.ValueKind != JsonValueKind.Object) return (null, "datos debe ser un objeto.");
        // A+W manda PESOSMX (distingue mayúsculas); el ERP solo lleva inventario en pesos.
        if (Texto(datos, "moneda")?.Trim() is { Length: > 0 } moneda && moneda is not ("PESOSMX" or "MXN"))
            return (null, $"moneda no soportada: {moneda} (solo PESOSMX).");
        var sucursal = Texto(datos, "sucursal");
        if (string.IsNullOrWhiteSpace(sucursal)) return (null, "datos.sucursal es requerida.");
        // La reclamación agrupa por línea de la compra (lineas[].grupos[]); las demás por almacén (almacenes[].grupos[]).
        var contenedor = tipo == Reclamacion ? "lineas" : "almacenes";
        if (!datos.TryGetProperty(contenedor, out var almacenes) || almacenes.ValueKind != JsonValueKind.Array
            || almacenes.GetArrayLength() == 0)
            return (null, $"{contenedor} debe ser un arreglo con al menos un elemento.");
        var manual = tipo is "SALIDA_MANUAL" or EntradaManual;

        var items = new List<(string Articulo, decimal? Ancho, decimal? Alto, decimal Piezas, decimal? M2, string Ruta,
            LlaveAlmacenApp? Almacen, string? Lote, decimal? CostoM2, decimal? CostoPieza)>();
        var i = 0;
        foreach (var alm in almacenes.EnumerateArray())
        {
            if (alm.ValueKind != JsonValueKind.Object) return (null, $"{contenedor}[{i}] debe ser un objeto.");
            var llave = Llave(alm);
            if (!alm.TryGetProperty("grupos", out var grupos) || grupos.ValueKind != JsonValueKind.Array
                || grupos.GetArrayLength() == 0)
                return (null, $"{contenedor}[{i}].grupos debe ser un arreglo con al menos un elemento.");
            var j = 0;
            foreach (var g in grupos.EnumerateArray())
            {
                var p = $"{contenedor}[{i}].grupos[{j}]";
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
                // La entrada y la salida manual traen costo: promedio de A+W por m² (vidrio) o por pieza (por cantidad; también la hoja
                // si A+W la costea por pieza, v9). Desde v9 es informativo (el ERP valúa con su promedio); la entrada lo usa solo si el
                // ERP aún no tiene promedio de esa medida.
                decimal? costoM2 = null, costoPieza = null;
                if (manual)
                {
                    if (!TryNumero(g, "costoM2", out costoM2)) return (null, $"{p}.costoM2 debe ser numérico.");
                    if (!TryNumero(g, "costoPieza", out costoPieza)) return (null, $"{p}.costoPieza debe ser numérico.");
                    if (costoM2 is < 0 || costoPieza is < 0) return (null, $"{p}.costoM2 y {p}.costoPieza no pueden ser negativos.");
                    if (ancho is null && costoPieza is null && costoM2 is not null) return (null, $"{p} no lleva medida: el costo va en costoPieza.");
                }
                items.Add((articulo, ancho, alto, piezas.Value, m2, p, llave, Texto(g, "lote"), costoM2, costoPieza));
                j++;
            }
            i++;
        }

        var sp = await usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo) return (null, "El usuario de servicio del ERP no está disponible.");

        var resuelto = await resolver.ResolverAsync(sucursal,
            items.Select(x => new GrupoSalidaApp(x.Articulo, x.Ancho, x.Alto)).ToList(), ct);
        if (resuelto.Error is not null) return (null, resuelto.Error);

        if (!TryClave(datos, "area", out var area)) return (null, "datos.area debe ser texto o numérico.");
        if (!TryClave(datos, "equipo", out var equipo)) return (null, "datos.equipo debe ser texto o numérico.");
        // Jorge (TI), 11-nov: la rotura y la salida manual no son de ninguna mesa; son del área de Almacén. El centro sale de la
        // configuración por sucursal, almacén y motivo (CentroAlmacen), solo si el mensaje no trae área ni equipo.
        if (area is null && equipo is null && tipo is "ROTURA" or "SALIDA_MANUAL"
            && CentroAlmacen(centrosAlmacen, sucursal, Llave(almacenes[0])?.Tipo, Texto(datos, "motivoClave")) is { } cfg)
            (area, equipo) = cfg;
        Guid? centro = null;
        var tipoMov = tipo switch
        {
            AlmacenesAppCambio.Tipo => TipoMovimiento.TrasladoInterno,
            EntradaManual => TipoMovimiento.AjustePositivo,
            _ => Tipos[tipo],
        };
        // El traslado (CAMBIO_ALMACEN), la entrada y la reclamación (va contra el proveedor, no contra un área) no llevan área ni equipo.
        if (dimensiones is not null && tipo is not (AlmacenesAppCambio.Tipo or EntradaManual or Reclamacion)
            && (area is not null || equipo is not null || exigirAreaEquipo))
        {
            var causa = SalidaInventarioRegistradaIntegrationEvent.CausaPorDefecto(tipoMov);
            var d = await dimensiones.ResolverAsync(new SolicitudDimensionesSalida(sp.EmpresaId, resuelto.SucursalId, causa,
                DateOnly.FromDateTime(DateTime.UtcNow), area, equipo), ct);
            if (d.Error is not null) return (null, d.Error);
            if (d.Faltante is not null && exigirAreaEquipo) return (null, d.Faltante);
            centro = d.CentroCostoId;
        }

        var motivo = $"App Almacenes {tipo} {Texto(datos, "movimiento")}".TrimEnd();
        var detalle = tipo == "AJUSTE_SALIDA"
            ? string.Concat(
                // El descuento llega al validar el documento previo (no al cortar); el ERP no reserva y solo lo deja en el motivo.
                DocumentoPrevio(datos) is { Length: > 0 } dp ? $" documento previo {dp}" : "",
                // v9: mesa y jobid van en renglones[]; pedidoAw es número (en datos y en cada renglón).
                DeRenglones(datos, "mesa") is { Length: > 0 } mesa ? $" mesa {mesa}" : "",
                DeRenglones(datos, "jobid") is { Length: > 0 } job ? $" job {job}" : "",
                (Valor(datos, "pedidoAw") ?? DeRenglones(datos, "pedidoAw")) is { Length: > 0 } ped ? $" pedido {ped}" : "")
            : tipo == Reclamacion
            ? string.Concat(
                Texto(datos, "reclamacion") is { Length: > 0 } rp ? $" {rp.Trim()}" : "",
                Texto(datos, "entrada") is { Length: > 0 } en ? $" entrada {en.Trim()}" : "",
                Texto(datos, "compra") is { Length: > 0 } oc ? $" compra {oc.Trim()}" : "",
                MotivosDeLineas(datos) is { Length: > 0 } mots ? $": {mots}" : "")
            : string.Concat(
                Texto(datos, "motivo") is { Length: > 0 } mot ? $": {mot.Trim()}" : "",
                Texto(datos, "motivoClave") is { Length: > 0 } clave ? $" [{clave.Trim()}]" : "");
        // v9: la entrada o salida manual que nace sola (cancelación de entrada, devolución de venta, regreso de préstamo) dice su origen;
        // la reclamación nace de su entrada. Con él se busca el costo del movimiento original.
        var origen = Origen(datos, tipo);
        if (origen is not null && tipo != Reclamacion) detalle += $" (origen {origen.Tipo} {origen.Referencia})";
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
            // Salidas sin costo: SalidaAppAlmacenes toma el promedio del saldo del ERP (v9). La entrada manual lleva el costo de A+W
            // en la unidad del saldo (m² en vidrio) como respaldo, por si el ERP aún no tiene promedio de esa medida.
            var respaldo = tipo != EntradaManual ? null
                : r.AreaHojaM2 is { } area1 ? x.CostoM2 ?? x.CostoPieza / area1
                : x.CostoPieza ?? x.CostoM2;
            salidas.Add(new SalidaApp(tipoMov, resuelto.SucursalId, r.ArticuloId, r.UnidadMedida, r.ProductoAwVarianteId,
                cantidad, respaldo, motivo, r.AnchoMm, r.AltoMm, ArticuloAw: x.Articulo.Trim(), CentroCostoId: centro,
                Almacen: x.Almacen, Lote: x.Lote,
                // La reclamación carga a Ajuste (no a Consumo) mientras CxP no la cobre al proveedor (flujo 8.B).
                Causa: tipo == Reclamacion ? "salida_manual" : null,
                // v9: clave fija del motivo, como dato del movimiento (sin catálogo: una clave nueva se guarda tal cual).
                MotivoClave: Texto(datos, "motivoClave")));
        }
        return (new Resuelta(sp.EmpresaId, sp.Id, salidas, origen), null);
    }

    /// <summary>
    /// <c>datos.origen</c> (objeto, v9): CANCELACION_ENTRADA → su <c>entrada</c>; DEVOLUCION_VENTA y REGRESO_PRESTAMO → su
    /// <c>salidaOriginal</c>. La reclamación → su <c>entrada</c>. Un <c>origen</c> de texto (AJUSTE_SALIDA: «CORTE») no es esto.
    /// </summary>
    private static OrigenApp? Origen(JsonElement datos, string tipo)
    {
        if (tipo == Reclamacion)
            return Texto(datos, "entrada")?.Trim() is { Length: > 0 } en ? new OrigenApp(Reclamacion, en) : null;
        if (!datos.TryGetProperty("origen", out var o) || o.ValueKind != JsonValueKind.Object
            || Texto(o, "tipo")?.Trim() is not { Length: > 0 } t) return null;
        var referencia = t == "CANCELACION_ENTRADA" ? Texto(o, "entrada") : Texto(o, "salidaOriginal");
        return referencia?.Trim() is { Length: > 0 } r ? new OrigenApp(t, r) : null;
    }

    /// <summary>
    /// SALIDA_MANUAL que cancela una entrada cerrada (v9): <c>origen.tipo = CANCELACION_ENTRADA</c>. Devuelve el folio de la entrada
    /// (<c>origen.entrada</c>, EN-…) y el <c>origen.motivo</c> libre; null si el mensaje no es esa cancelación.
    /// </summary>
    public static (string Entrada, string? Motivo)? CancelacionEntrada(JsonElement datos, string tipo) =>
        tipo == "SALIDA_MANUAL" && datos.ValueKind == JsonValueKind.Object && Origen(datos, tipo) is { Tipo: "CANCELACION_ENTRADA" } o
            ? (o.Referencia, Texto(datos.GetProperty("origen"), "motivo"))
            : null;

    /// <summary>
    /// Llave del almacén de un renglón (M-446): <c>almacenGeneral.tipo</c> + <c>almacenGeneral.linea</c> (vacía en herraje). Sin
    /// tipo → null (el ERP resuelve como antes, por sucursal). El id del almacén general es dato acompañante y no se usa.
    /// </summary>
    /// <summary>
    /// Área/equipo por omisión de la rotura y la salida manual. Llaves de la más a la menos específica:
    /// <c>SUCURSAL|TIPO_ALMACEN|MOTIVO</c>, <c>SUCURSAL|MOTIVO</c>, <c>SUCURSAL|TIPO_ALMACEN</c>, <c>SUCURSAL</c>; valor <c>AREA/EQUIPO</c>
    /// (el equipo es opcional). Sin coincidencia → null (sigue como hoy: la póliza queda en Rechazos si la cuenta los exige).
    /// </summary>
    public static (string? Area, string? Equipo)? CentroAlmacen(
        IReadOnlyDictionary<string, string>? mapa, string sucursal, string? tipoAlmacen, string? motivoClave)
    {
        if (mapa is null || mapa.Count == 0) return null;
        static string N(string? v) => (v ?? "").Trim().ToUpperInvariant();
        var (s, t, m) = (N(sucursal), N(tipoAlmacen), N(motivoClave));
        string[] llaves = [$"{s}|{t}|{m}", $"{s}|{m}", $"{s}|{t}", s];
        foreach (var llave in llaves)
        {
            var valor = mapa.FirstOrDefault(kv => N(kv.Key) == llave).Value;
            if (string.IsNullOrWhiteSpace(valor)) continue;
            var partes = valor.Split('/', 2, StringSplitOptions.TrimEntries);
            return (partes[0].Length > 0 ? partes[0] : null, partes.Length > 1 && partes[1].Length > 0 ? partes[1] : null);
        }
        return null;
    }

    internal static LlaveAlmacenApp? Llave(JsonElement contenedor, string propiedad = "almacenGeneral") =>
        contenedor.TryGetProperty(propiedad, out var ag) && ag.ValueKind == JsonValueKind.Object
        && Texto(ag, "tipo") is { } tipo && !string.IsNullOrWhiteSpace(tipo)
            ? new LlaveAlmacenApp(tipo, Texto(ag, "linea"))
            : null;

    // Texto o número; ausente, null, vacío u otro tipo → null.
    private static string? Valor(JsonElement o, string n) =>
        o.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            && v.ToString().Trim() is { Length: > 0 } t ? t : null;

    // Valores distintos de un campo en datos (forma anterior) o en renglones[] (v9), separados por coma.
    private static string DeRenglones(JsonElement datos, string campo) =>
        Valor(datos, campo) ?? (datos.TryGetProperty("renglones", out var rs) && rs.ValueKind == JsonValueKind.Array
            ? string.Join(",", rs.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object)
                .Select(r => Valor(r, campo)).Where(x => x is not null).Distinct())
            : "");

    // Motivos de las líneas de la reclamación («Rota», «No llegó»…), sin repetir.
    private static string MotivosDeLineas(JsonElement datos) =>
        datos.TryGetProperty("lineas", out var ls) && ls.ValueKind == JsonValueKind.Array
            ? string.Join(", ", ls.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.Object)
                .Select(l => Texto(l, "motivo")?.Trim()).Where(m => !string.IsNullOrEmpty(m)).Distinct())
            : "";

    // documentoPrevio: el contrato aún no fija su forma; solo se toma si viene como texto o número (otra forma se ignora, sin inventar campos).
    private static string? DocumentoPrevio(JsonElement datos)
    {
        if (!datos.TryGetProperty("documentoPrevio", out var d)) return null;
        var t = d.ValueKind switch { JsonValueKind.String => d.GetString(), JsonValueKind.Number => d.GetRawText(), _ => null };
        return t?.Trim() is { Length: > 60 } largo ? largo[..60] : t?.Trim();
    }

    // Clave de centro: texto o número; ausente, null o vacía → null. Otro tipo → false.
    private static bool TryClave(JsonElement o, string n, out string? valor)
    {
        valor = null;
        if (!o.TryGetProperty(n, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        var t = v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null };
        if (t is null) return false;
        valor = string.IsNullOrWhiteSpace(t) ? null : t.Trim();
        return true;
    }

    internal static string? Texto(JsonElement o, string n) =>
        o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Ausente o null → true con null; presente con tipo distinto de número → false.
    internal static bool TryNumero(JsonElement o, string n, out decimal? valor)
    {
        valor = null;
        if (!o.TryGetProperty(n, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var d)) return false;
        valor = d;
        return true;
    }
}
