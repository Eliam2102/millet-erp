using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Cotizaciones;

/// <summary>
/// Partida cotizada de una <see cref="Cotizacion"/>: unidad cotizada, factor a
/// la unidad de la requisición, precio y días de entrega.
/// </summary>
public sealed class LineaCotizacion : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid CotizacionId { get; private set; }
    public Guid AggregateRootId => CotizacionId;

    public Guid LineaRequisicionId { get; private set; }
    public string UnidadCotizada { get; private set; } = string.Empty;
    public decimal FactorAUnidadRq { get; private set; }
    public decimal PrecioUnitario { get; private set; }
    public int? DiasEntrega { get; private set; }

    public decimal PrecioPorUnidadRequisicion
        => NormalizadorCotizacion.PrecioPorUnidadRequisicion(PrecioUnitario, FactorAUnidadRq);

    /// <summary>Constructor para EF Core.</summary>
    private LineaCotizacion() { }

    internal LineaCotizacion(Guid id, Guid cotizacionId, LineaCotizacionDatos datos) : base(id)
    {
        if (datos.LineaRequisicionId == Guid.Empty)
        {
            throw new BusinessRuleException("COT_PARTIDA_AJENA", "La partida no pertenece a la requisición.");
        }

        if (string.IsNullOrWhiteSpace(datos.UnidadCotizada) || datos.UnidadCotizada.Trim().Length > 10)
        {
            throw new BusinessRuleException("COT_UNIDAD_REQUERIDA",
                "Indica la unidad cotizada de cada partida (máximo 10 caracteres).");
        }

        if (datos.FactorAUnidadRq <= 0)
        {
            throw new BusinessRuleException("COT_FACTOR_INVALIDO", "El factor de conversión debe ser mayor a cero.");
        }

        if (string.Equals(datos.UnidadCotizada.Trim(), datos.UnidadRequisicion.Trim(), StringComparison.OrdinalIgnoreCase)
            && datos.FactorAUnidadRq != 1m)
        {
            throw new BusinessRuleException("COT_FACTOR_MISMA_UNIDAD",
                "Si la unidad cotizada es la misma de la requisición, el factor es 1.");
        }

        if (datos.PrecioUnitario <= 0)
        {
            throw new BusinessRuleException("COT_PRECIO_INVALIDO", "El precio debe ser mayor a cero.");
        }

        if (datos.DiasEntrega is < 0 or > 365)
        {
            throw new BusinessRuleException("COT_DIAS_ENTREGA_INVALIDOS", "Los días de entrega deben estar entre 0 y 365.");
        }

        if (NormalizadorCotizacion.PrecioPorUnidadRequisicion(datos.PrecioUnitario, datos.FactorAUnidadRq) <= 0)
        {
            throw new BusinessRuleException("COT_PRECIO_INVALIDO", "El precio debe ser mayor a cero.");
        }

        CotizacionId = cotizacionId;
        LineaRequisicionId = datos.LineaRequisicionId;
        UnidadCotizada = datos.UnidadCotizada.Trim().ToUpperInvariant();
        FactorAUnidadRq = datos.FactorAUnidadRq;
        PrecioUnitario = datos.PrecioUnitario;
        DiasEntrega = datos.DiasEntrega;
    }
}
