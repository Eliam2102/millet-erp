using System.Text.Json;
using System.Text.Json.Serialization;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Integration.AlmCompra;

/// <summary>
/// Línea de COMPRA_RECIBIDA / COMPRA_MODIFICADA (contrato v8 §2). Los nulos no viajan. La línea se identifica por
/// <c>lineaErp</c> + <c>requisicion</c> + <c>posicion</c>; el ERP no manda <c>articuloAlmacenAw</c> (v8, punto 10 cerrado: la línea
/// es artículo + medida en cualquier orientación; el campo es informativo si viene).
/// </summary>
public sealed record AlmCompraLinea(
    string LineaErp, string? Requisicion, int? Posicion, int ArticuloAw, int? ArticuloAlmacenAw,
    int? Ancho, int? Alto, int Piezas, string? Lote, string? Pedimento, string? Etiqueta,
    decimal? CostoM2, decimal? CostoPieza,
    // Opcional e informativo (v11 puntos 8 y 11): promedio móvil del ERP por m² tras la compra (M-149), en la moneda de la compra.
    // Solo vidrio en m²; sin valor no viaja.
    decimal? CostoMedioM2 = null);

/// <summary>
/// Contenido de la compra completa (no en diferencias, regla de TI para COMPRA_MODIFICADA). La recepción (hojas, lote, rack)
/// no viaja: es de Almacenes (TI, 5-nov).
/// </summary>
public sealed record AlmCompraDatos(
    string Folio, int ProveedorClaveAw, string ProveedorNombre, string MonedaAw, decimal? TipoCambio,
    DateOnly FechaCompra, IReadOnlyList<AlmCompraLinea> Lineas);

/// <summary>Armado del sobre v1 y reglas puras de cantidad y costo.</summary>
public static class AlmCompraSobre
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Moneda ISO del ERP → código de A+W: el mapeo configurado (<c>MapeoMonedaAw</c>) o, sin él, PESOSMX para MXN y el ISO tal cual.</summary>
    public static string MonedaAw(string iso, string? mapeada) =>
        !string.IsNullOrWhiteSpace(mapeada) ? mapeada : iso == "MXN" ? "PESOSMX" : iso;

    /// <summary>
    /// mensajeId determinista: OC + tipo + ordinal (≤ 64 caracteres, límite del contrato). Si el folio no cabe, usa el id de la OC.
    /// </summary>
    public static string MensajeId(string folioOc, Guid ocId, string tipo, int ordinal)
    {
        var letra = tipo switch { "COMPRA_RECIBIDA" => "R", "COMPRA_CANCELADA" => "C", _ => "M" };
        var id = $"ERP-{folioOc}-{letra}{ordinal}";
        return id.Length <= 64 ? id : $"ERP-{ocId:N}-{letra}{ordinal}";
    }

    public static string Armar(string mensajeId, string tipo, DateTimeOffset fecha, string referencia, object datos) =>
        JsonSerializer.Serialize(new
        {
            mensajeId, tipo, version = 1, fecha, origen = "ERP", referencia, datos,
        }, Json);

    /// <summary>
    /// COMPRA_CANCELADA.datos (v8): <c>folio</c> y <c>motivo</c> (≤ 900) obligatorios; <c>fechaCancelacion</c> (ISO 8601) opcional e
    /// informativa: queda en la bitácora de Almacenes, que registra la cancelación con la hora en que la procesa.
    /// </summary>
    public static object DatosCancelada(string folio, string? motivo, DateTimeOffset? fechaCancelacion = null)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("ALM_COMPRA_CANCELACION_SIN_MOTIVO", "COMPRA_CANCELADA exige un motivo; Almacenes la rechaza sin él.");
        return new { folio, motivo = motivo.Trim() is { Length: > 900 } m ? m[..900] : motivo.Trim(), fechaCancelacion };
    }

    /// <summary>
    /// COMPRA_MODIFICADA: lo que no cambia (TI, 5-nov) debe coincidir con la COMPRA_RECIBIDA ya mandada: folio, proveedor y, por
    /// línea, lineaErp, requisicion, posicion, articuloAw y medidas (en cualquier orientación). Devuelve el primer desajuste o <c>null</c>.
    /// </summary>
    public static string? CampoFijoDistinto(string sobreRecibida, AlmCompraDatos d)
    {
        using var doc = JsonDocument.Parse(sobreRecibida);
        var o = doc.RootElement.GetProperty("datos");
        if (o.GetProperty("folio").GetString() != d.Folio) return "folio";
        if (o.GetProperty("proveedor").GetProperty("claveAw").GetInt32() != d.ProveedorClaveAw) return "proveedor";
        var antes = o.GetProperty("lineas").EnumerateArray().ToList();
        if (antes.Count != d.Lineas.Count) return "lineas (no se agregan ni se quitan)";
        foreach (var l in d.Lineas)
        {
            var a = antes.FirstOrDefault(x => x.GetProperty("lineaErp").GetString() == l.LineaErp);
            if (a.ValueKind == JsonValueKind.Undefined) return $"lineaErp {l.LineaErp}";
            string? Txt(string n) => a.TryGetProperty(n, out var v) ? v.GetString() : null;
            int? Num(string n) => a.TryGetProperty(n, out var v) ? v.GetInt32() : null;
            if (Txt("requisicion") != l.Requisicion || Num("posicion") != l.Posicion) return $"requisicion/posicion de la línea {l.LineaErp}";
            if (Num("articuloAw") != l.ArticuloAw) return $"articuloAw de la línea {l.LineaErp}";
            if (Math.Max(Num("ancho") ?? 0, Num("alto") ?? 0) != Math.Max(l.Ancho ?? 0, l.Alto ?? 0)
                || Math.Min(Num("ancho") ?? 0, Num("alto") ?? 0) != Math.Min(l.Ancho ?? 0, l.Alto ?? 0)) return $"medidas de la línea {l.LineaErp}";
        }
        return null;
    }

    /// <summary>Datos de COMPRA_RECIBIDA y COMPRA_MODIFICADA (misma forma, completa).</summary>
    public static object DatosCompra(AlmCompraDatos d) => new
    {
        folio = d.Folio,
        proveedor = new { claveAw = d.ProveedorClaveAw, nombre = d.ProveedorNombre },
        moneda = d.MonedaAw, // código de A+W (PESOSMX para pesos), no el ISO
        tipoCambio = d.TipoCambio is > 0 ? d.TipoCambio : null, // opcional; si viene debe ser > 0
        fechaCompra = d.FechaCompra,
        lineas = d.Lineas,
    };

    /// <summary>¿El <c>datos</c> de un sobre ya enviado es idéntico a la compra actual? Evita mandar una COMPRA_MODIFICADA sin cambios.</summary>
    public static bool TraeLosMismosDatos(string sobre, AlmCompraDatos d)
    {
        if (string.IsNullOrEmpty(sobre)) return false;
        using var doc = JsonDocument.Parse(sobre);
        return doc.RootElement.GetProperty("datos").GetRawText() == JsonSerializer.Serialize(DatosCompra(d), Json);
    }

    /// <summary>
    /// Piezas, medida y costos de una línea. Unidad M2: la cantidad son m² y las piezas salen de la superficie de la hoja;
    /// otra unidad: la cantidad son piezas. <c>costoM2</c> = precio por m² (en vidrio siempre); <c>costoPieza</c> = precio por pieza.
    /// <paramref name="subtotalNeto"/> ya trae el descuento de la línea, en la moneda de la OC.
    /// </summary>
    public static (int Piezas, int RecibidasPiezas, decimal? CostoM2, decimal? CostoPieza) Calcular(
        string unidad, decimal cantidad, decimal cantidadRecibida, decimal subtotalNeto, decimal? anchoMm, decimal? altoMm)
    {
        decimal? area = anchoMm > 0 && altoMm > 0 ? anchoMm * altoMm / 1_000_000m : null;
        var unitario = cantidad > 0 ? subtotalNeto / cantidad : 0m;
        if (string.Equals(unidad, "M2", StringComparison.OrdinalIgnoreCase))
        {
            if (area is null)
                throw new BusinessRuleException("ALM_COMPRA_SIN_MEDIDA", "La línea está en m² y no tiene medida de hoja para calcular las piezas.");
            return (Entero(cantidad / area.Value), Entero(cantidadRecibida / area.Value),
                Math.Round(unitario, 4), Math.Round(unitario * area.Value, 4));
        }
        return (Entero(cantidad), Entero(cantidadRecibida), area is null ? null : Math.Round(unitario / area.Value, 4), Math.Round(unitario, 4));
    }

    /// <summary>
    /// Medida girada = misma hoja (Jorge 30-oct y 1-nov): 3302 × 6100 y 6100 × 3302 son una sola existencia. Junta las
    /// líneas que solo difieren en la orientación (mismo artículo, lote, pedimento, RQ y costo), sumando piezas;
    /// la línea que queda conserva la orientación de la primera. Sin medida (material por cantidad) no se toca.
    /// </summary>
    public static IReadOnlyList<AlmCompraLinea> AgruparMedidaGirada(IEnumerable<AlmCompraLinea> lineas) => lineas
        .GroupBy(l => (l.Ancho is null ? l.LineaErp : null, l.Requisicion, l.Posicion, l.ArticuloAw,
            Math.Max(l.Ancho ?? 0, l.Alto ?? 0), Math.Min(l.Ancho ?? 0, l.Alto ?? 0), l.Lote, l.Pedimento, l.CostoM2, l.CostoPieza))
        .Select(g => g.Count() == 1 ? g.First() : g.First() with { Piezas = g.Sum(x => x.Piezas) })
        .ToList();

    /// <summary>Pedimento con el formato del contrato v6 (<c>26 47 3456 6001234</c>); la OC lo guarda sin espacios.</summary>
    public static string? FormatoPedimento(string? p) =>
        p is { Length: 15 } && p.All(char.IsAsciiDigit) ? $"{p[..2]} {p[2..4]} {p[4..8]} {p[8..]}" : p;

    private static int Entero(decimal x)
    {
        var r = Math.Round(x, MidpointRounding.AwayFromZero);
        if (Math.Abs(x - r) > 0.01m)
            throw new BusinessRuleException("ALM_COMPRA_PIEZAS_NO_ENTERAS", $"La cantidad no corresponde a un número entero de piezas ({x:0.####}).");
        return (int)r;
    }
}
