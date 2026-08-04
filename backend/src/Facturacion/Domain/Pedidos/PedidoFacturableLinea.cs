using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Pedidos;

/// <summary>
/// Línea de un <see cref="PedidoFacturable"/> (§5 diseño). Entidad hija: sólo se
/// crea vía <see cref="PedidoFacturable.AgregarLinea"/>. En captura manual el
/// producto se referencia por id + snapshot de descripción/claves SAT que aporta
/// el operador (la validación contra el master de Producto se hará cuando
/// DatosMaestros sea real — cerrado por ADR-0048 PR2: ProductosReadAdapter).
/// </summary>
public sealed class PedidoFacturableLinea : BaseEntity, IBelongsToAggregate
{
    public Guid PedidoFacturableId { get; private set; }
    public int Posicion { get; private set; }

    public Guid? ProductoId { get; private set; }
    public string ProductoDescripcion { get; private set; } = string.Empty;
    public string? ClaveProdServSat { get; private set; }
    public string? ClaveUnidadSat { get; private set; }

    public decimal Cantidad { get; private set; }
    public decimal Precio { get; private set; }
    public decimal Descuento { get; private set; }

    /// <summary>Importe = Cantidad × Precio − Descuento.</summary>
    public decimal Importe { get; private set; }

    /// <summary>
    /// Indicador "requiere pedimento" por línea (§3.bis.5). Lo aporta el pedido,
    /// no el catálogo de Producto. F7 lo consume; en F1-PR2 default false.
    /// </summary>
    public bool RequierePedimento { get; private set; }

    /// <summary>
    /// Componentes/BOM de la posición del origen (jsonb, informativo — §5
    /// diseño, materializado por ADR-0048 D8). No participa en importes ni
    /// en el CFDI; consultable para costos/auditoría. <c>null</c> en captura
    /// manual y en orígenes sin BOM.
    /// </summary>
    public string? BomJson { get; private set; }

    /// <summary>
    /// Tasa de IVA trasladado de la línea (fracción — FAC-DET-PR2). Origen
    /// A+W: tasa del documento (exportaciones con 0). Manual: se resuelve al
    /// capturar con precedencia artículo (<c>ProductoAw.TasaIvaTraslado</c>)
    /// &gt; default de empresa. <c>null</c> = sin tasa persistida; el detalle
    /// cae al master del artículo (query de prefill).
    /// </summary>
    public decimal? TasaIva { get; private set; }

    public Guid AggregateRootId => PedidoFacturableId;

    private PedidoFacturableLinea() { }

    internal PedidoFacturableLinea(
        Guid id,
        Guid pedidoFacturableId,
        int posicion,
        Guid? productoId,
        string productoDescripcion,
        string? claveProdServSat,
        string? claveUnidadSat,
        decimal cantidad,
        decimal precio,
        decimal descuento,
        bool requierePedimento,
        string? bomJson = null,
        decimal? tasaIva = null) : base(id)
    {
        if (posicion <= 0)
            throw new BusinessRuleException("PEDIDO_LINEA_POSICION_INVALIDA", "La posición debe ser >= 1.");
        if (string.IsNullOrWhiteSpace(productoDescripcion))
            throw new BusinessRuleException("PEDIDO_LINEA_DESCRIPCION_VACIA", "La descripción del producto es obligatoria.");
        if (cantidad <= 0)
            throw new BusinessRuleException("PEDIDO_LINEA_CANTIDAD_INVALIDA", "La cantidad debe ser > 0.");
        if (precio < 0)
            throw new BusinessRuleException("PEDIDO_LINEA_PRECIO_INVALIDO", "El precio no puede ser negativo.");
        if (descuento < 0)
            throw new BusinessRuleException("PEDIDO_LINEA_DESCUENTO_INVALIDO", "El descuento no puede ser negativo.");
        if (tasaIva is < 0 or > 1)
            throw new BusinessRuleException("PEDIDO_LINEA_TASA_IVA_INVALIDA",
                "La tasa de IVA debe ser fracción entre 0 y 1 (p.ej. 0.16).");

        var importe = Math.Round(cantidad * precio - descuento, 2, MidpointRounding.AwayFromZero);
        if (importe < 0)
            throw new BusinessRuleException("PEDIDO_LINEA_DESCUENTO_EXCEDE", "El descuento no puede exceder cantidad × precio.");

        PedidoFacturableId = pedidoFacturableId;
        Posicion = posicion;
        ProductoId = productoId;
        ProductoDescripcion = productoDescripcion;
        ClaveProdServSat = claveProdServSat;
        ClaveUnidadSat = claveUnidadSat;
        Cantidad = cantidad;
        Precio = precio;
        Descuento = descuento;
        Importe = importe;
        RequierePedimento = requierePedimento;
        BomJson = bomJson;
        TasaIva = tasaIva;
    }
}
