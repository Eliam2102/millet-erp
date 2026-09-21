using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

/// <summary>
/// Línea (concepto) de una <see cref="FacturaProveedor"/> (§4.3 del
/// 00-levantamiento). Internal por convención del agregado: solo se
/// crea desde la factory del agregado raíz.
/// </summary>
public sealed class LineaFacturaProveedor : BaseEntity, IBelongsToAggregate, IAuditable
{
    public Guid FacturaProveedorId { get; private set; }
    public int Posicion { get; private set; }

    public Guid? ArticuloId { get; private set; }
    public string? ClaveProdServ { get; private set; }
    public string Descripcion { get; private set; } = default!;

    public decimal Cantidad { get; private set; }
    public string ClaveUnidad { get; private set; } = default!;
    public string? Unidad { get; private set; }

    public decimal PrecioUnitario { get; private set; }
    public decimal Importe { get; private set; }
    public decimal? Descuento { get; private set; }

    public Guid? LineaOcId { get; private set; }
    public Guid? ConceptoContableId { get; private set; }

    public Guid AggregateRootId => FacturaProveedorId;

    private LineaFacturaProveedor() { }

    internal LineaFacturaProveedor(
        Guid id,
        Guid facturaProveedorId,
        int posicion,
        Guid? articuloId,
        string? claveProdServ,
        string descripcion,
        decimal cantidad,
        string claveUnidad,
        string? unidad,
        decimal precioUnitario,
        decimal importe,
        decimal? descuento,
        Guid? lineaOcId,
        Guid? conceptoContableId) : base(id)
    {
        if (posicion <= 0)
            throw new BusinessRuleException("LINEA_POSICION_INVALIDA", "La posición debe ser >= 1.");
        if (cantidad <= 0)
            throw new BusinessRuleException("LINEA_CANTIDAD_INVALIDA", "La cantidad debe ser > 0.");
        if (importe < 0)
            throw new BusinessRuleException("LINEA_IMPORTE_NEGATIVO", "El importe no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(descripcion))
            throw new BusinessRuleException("LINEA_DESCRIPCION_VACIA", "La descripción es obligatoria.");

        FacturaProveedorId = facturaProveedorId;
        Posicion = posicion;
        ArticuloId = articuloId;
        ClaveProdServ = claveProdServ;
        Descripcion = descripcion;
        Cantidad = cantidad;
        ClaveUnidad = claveUnidad;
        Unidad = unidad;
        PrecioUnitario = precioUnitario;
        Importe = importe;
        Descuento = descuento;
        LineaOcId = lineaOcId;
        ConceptoContableId = conceptoContableId;
    }
}
