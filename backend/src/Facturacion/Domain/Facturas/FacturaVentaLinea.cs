using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Facturas;

/// <summary>
/// Línea de una <see cref="FacturaVenta"/> (§4.2 diseño). Entidad hija: solo se
/// crea vía <see cref="FacturaVenta.AgregarLinea"/>. Calcula sus propios
/// importes de impuestos a partir de las tasas en el constructor (fail-fast).
///
/// <para>
/// Los datos aduaneros / <c>requiere_pedimento</c> (§3.bis.5) entran en F7; en
/// F1 la línea sólo modela producto + clave SAT + cantidad + precio + descuento
/// + impuestos.
/// </para>
/// </summary>
public sealed class FacturaVentaLinea : BaseEntity, IBelongsToAggregate, IAuditable
{
    public Guid FacturaVentaId { get; private set; }
    public int Posicion { get; private set; }

    public Guid? ProductoId { get; private set; }
    public string ClaveProdServSat { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public string ClaveUnidadSat { get; private set; } = string.Empty;

    public decimal Cantidad { get; private set; }
    public decimal ValorUnitario { get; private set; }
    public decimal Descuento { get; private set; }

    /// <summary>Importe de la línea = Cantidad × ValorUnitario (antes del descuento, como exige el CFDI).</summary>
    public decimal Importe { get; private set; }

    /// <summary>c_ObjetoImp del SAT: 01 No objeto, 02 Sí objeto, 03 Sí objeto y no obligado.</summary>
    public string ObjetoImp { get; private set; } = "02";

    public decimal? TasaIvaTraslado { get; private set; }
    public decimal ImpuestoTrasladadoImporte { get; private set; }

    public decimal? TasaRetencionIva { get; private set; }
    public decimal? TasaRetencionIsr { get; private set; }
    public decimal RetencionTotalImporte { get; private set; }

    /// <summary>
    /// 86e3zcbjw: concepto de la retención de IVA que nos hace el cliente (<see cref="ConceptosRetencionCliente"/>); Contabilidad lleva
    /// la retención a la cuenta del concepto. Sin concepto se deduce de la tasa cuando no hay duda (4 % fletes, 6 % servicios).
    /// </summary>
    public string? ConceptoRetencion { get; private set; }

    // ---- Datos aduaneros / pedimento (§3.bis.5, F7-PR2) ----
    /// <summary>True si la línea es producto de importación en su primera venta (art. 29-A CFF).</summary>
    public bool RequierePedimento { get; private set; }

    /// <summary>Número del pedimento aduanero (null hasta aplicarlo).</summary>
    public string? Pedimento { get; private set; }

    public DateOnly? FechaDocAduanero { get; private set; }
    public string? IdentificacionMercancia { get; private set; }

    public string? Referencia { get; private set; }
    public decimal? PesoKg { get; private set; }
    public decimal? AreaPie2 { get; private set; }

    public Guid AggregateRootId => FacturaVentaId;

    private FacturaVentaLinea() { }

    internal FacturaVentaLinea(
        Guid id,
        Guid facturaVentaId,
        int posicion,
        Guid? productoId,
        string claveProdServSat,
        string descripcion,
        string claveUnidadSat,
        decimal cantidad,
        decimal valorUnitario,
        decimal descuento,
        string objetoImp,
        decimal? tasaIvaTraslado,
        decimal? tasaRetencionIva,
        decimal? tasaRetencionIsr,
        bool requierePedimento = false, string? referencia = null, decimal? pesoKg = null, decimal? areaPie2 = null,
        string? conceptoRetencion = null) : base(id)
    {
        (conceptoRetencion, tasaRetencionIva) = ConceptosRetencionCliente.Resolver(conceptoRetencion, tasaRetencionIva);
        if (posicion <= 0)
            throw new BusinessRuleException("LINEA_POSICION_INVALIDA", "La posición debe ser >= 1.");
        if (string.IsNullOrWhiteSpace(claveProdServSat))
            throw new BusinessRuleException("LINEA_SIN_CLAVE_SAT", "La línea requiere clave de producto/servicio SAT (sin clave SAT no se factura).");
        if (string.IsNullOrWhiteSpace(claveUnidadSat))
            throw new BusinessRuleException("LINEA_SIN_CLAVE_UNIDAD", "La línea requiere clave de unidad SAT.");
        if (string.IsNullOrWhiteSpace(descripcion))
            throw new BusinessRuleException("LINEA_DESCRIPCION_VACIA", "La descripción es obligatoria.");
        if (cantidad <= 0)
            throw new BusinessRuleException("LINEA_CANTIDAD_INVALIDA", "La cantidad debe ser > 0.");
        if (valorUnitario < 0)
            throw new BusinessRuleException("LINEA_VALOR_UNITARIO_INVALIDO", "El valor unitario no puede ser negativo.");
        if (descuento < 0)
            throw new BusinessRuleException("LINEA_DESCUENTO_INVALIDO", "El descuento no puede ser negativo.");

        var importe = Math.Round(cantidad * valorUnitario, 2, MidpointRounding.AwayFromZero);
        if (descuento > importe)
            throw new BusinessRuleException("LINEA_DESCUENTO_EXCEDE_IMPORTE", "El descuento no puede exceder el importe de la línea.");

        var baseGravable = importe - descuento;

        FacturaVentaId = facturaVentaId;
        Posicion = posicion;
        ProductoId = productoId;
        ClaveProdServSat = claveProdServSat;
        Descripcion = descripcion;
        ClaveUnidadSat = claveUnidadSat;
        Cantidad = cantidad;
        ValorUnitario = valorUnitario;
        Descuento = descuento;
        Importe = importe;
        ObjetoImp = string.IsNullOrWhiteSpace(objetoImp) ? "02" : objetoImp;
        TasaIvaTraslado = tasaIvaTraslado;
        ImpuestoTrasladadoImporte = Math.Round(baseGravable * (tasaIvaTraslado ?? 0m), 2, MidpointRounding.AwayFromZero);
        TasaRetencionIva = tasaRetencionIva;
        TasaRetencionIsr = tasaRetencionIsr;
        ConceptoRetencion = conceptoRetencion;
        RetencionTotalImporte = Math.Round(baseGravable * ((tasaRetencionIva ?? 0m) + (tasaRetencionIsr ?? 0m)), 2, MidpointRounding.AwayFromZero);
        RequierePedimento = requierePedimento;
        Referencia = DatosImpresion.Texto(referencia, 80, "Referencia de la partida");
        PesoKg = DatosImpresion.Medida(pesoKg);
        AreaPie2 = DatosImpresion.Medida(areaPie2);
    }

    /// <summary>
    /// Aplica el pedimento aduanero a una línea que lo requiere (§3.bis.5, F7-PR2).
    /// La invoca <see cref="FacturaVenta.AplicarPedimento"/> al completar el
    /// borrador retenido.
    /// </summary>
    internal void AplicarPedimento(string pedimento, DateOnly? fechaDocAduanero, string? identificacionMercancia)
    {
        if (string.IsNullOrWhiteSpace(pedimento))
            throw new BusinessRuleException("PEDIMENTO_INVALIDO", "El número de pedimento es obligatorio.");
        Pedimento = pedimento;
        FechaDocAduanero = fechaDocAduanero;
        IdentificacionMercancia = identificacionMercancia;
    }
}

/// <summary>
/// 86e3zcbjw · retenciones de IVA que nos hacen los clientes (tabla de Contabilidad del 5-nov). La tasa es sobre la base de la línea.
/// </summary>
public static class ConceptosRetencionCliente
{
    public const string Fletes = "FLETES";
    public const string Servicios = "SERVICIOS";
    public const string Desperdicio = "DESPERDICIO";
    public const string Importacion = "IMPORTACION";

    public static readonly IReadOnlyDictionary<string, decimal> Tasas = new Dictionary<string, decimal>
    {
        [Fletes] = 0.04m, [Servicios] = 0.06m, [Desperdicio] = 0.16m, [Importacion] = 0.16m,
    };

    /// <summary>
    /// Concepto y tasa de la línea: con concepto, la tasa es la suya (si se capturó otra, error); sin concepto, se deduce de la tasa
    /// cuando solo un concepto la usa (4 % y 6 %). El 16 % es de desperdicio o de importación: ahí el concepto lo elige quien factura.
    /// </summary>
    public static (string? Concepto, decimal? TasaIva) Resolver(string? concepto, decimal? tasaIva)
    {
        if (string.IsNullOrWhiteSpace(concepto))
        {
            if (tasaIva is not > 0m) return (null, tasaIva);
            var unicos = Tasas.Where(t => t.Value == tasaIva.Value).Select(t => t.Key).ToList();
            return (unicos.Count == 1 ? unicos[0] : null, tasaIva);
        }
        var clave = concepto.Trim().ToUpperInvariant();
        if (!Tasas.TryGetValue(clave, out var tasa))
            throw new BusinessRuleException("LINEA_CONCEPTO_RETENCION_INVALIDO",
                $"Concepto de retención desconocido: {concepto}. Usa fletes, servicios, desperdicio o importación.");
        if (tasaIva is { } capturada && capturada != tasa)
            throw new BusinessRuleException("LINEA_CONCEPTO_RETENCION_TASA",
                $"La retención de IVA de {clave} es de {tasa:P0}; la línea trae {capturada:P2}.");
        return (clave, tasa);
    }
}
