using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>
/// Variante de un <see cref="ProductoAw"/> (docs/integration/06 §5): una
/// medida/composición del mismo código A+W. Las medidas son nullables: nulo
/// significa "no informado", nunca 0.
/// </summary>
public sealed class ProductoAwVariante : BaseEntity, INotAudited
{
    public Guid ProductoAwId { get; private set; }
    public string ClaveVariante { get; private set; } = string.Empty;
    public decimal? AltoMm { get; private set; }
    public decimal? AnchoMm { get; private set; }
    public decimal? EspesorMm { get; private set; }
    public string? Composicion { get; private set; }

    private ProductoAwVariante() { }

    internal ProductoAwVariante(Guid id, Guid productoAwId, string claveVariante,
        decimal? altoMm, decimal? anchoMm, decimal? espesorMm, string? composicion) : base(id)
    {
        if (productoAwId == Guid.Empty)
            throw new BusinessRuleException("PRODUCTO_AW_VARIANTE_PRODUCTO_INVALIDO", "El producto es requerido.");
        if (string.IsNullOrWhiteSpace(claveVariante) || claveVariante.Length > 50)
            throw new BusinessRuleException("PRODUCTO_AW_VARIANTE_CLAVE_INVALIDA",
                "La clave de variante es requerida y no puede exceder 50 caracteres.");
        ProductoAwId = productoAwId;
        ClaveVariante = claveVariante;
        Asignar(altoMm, anchoMm, espesorMm, composicion);
    }

    /// <summary>Reemplaza medidas y composición (la clave es inmutable).</summary>
    internal void Asignar(decimal? altoMm, decimal? anchoMm, decimal? espesorMm, string? composicion)
    {
        if (altoMm is <= 0 || anchoMm is <= 0 || espesorMm is <= 0)
            throw new BusinessRuleException("PRODUCTO_AW_VARIANTE_MEDIDA_INVALIDA",
                "Las medidas deben ser mayores a 0 (o nulas si no se informan).");
        if (composicion is { Length: > 200 })
            throw new BusinessRuleException("PRODUCTO_AW_VARIANTE_COMPOSICION_INVALIDA",
                "La composición no puede exceder 200 caracteres.");
        AltoMm = altoMm;
        AnchoMm = anchoMm;
        EspesorMm = espesorMm;
        Composicion = composicion;
    }
}

/// <summary>Variante recibida de A+W para <see cref="ProductoAw.AplicarVariantes"/>.</summary>
public sealed record ProductoAwVarianteDato(
    string ClaveVariante, decimal? AltoMm, decimal? AnchoMm, decimal? EspesorMm, string? Composicion);
