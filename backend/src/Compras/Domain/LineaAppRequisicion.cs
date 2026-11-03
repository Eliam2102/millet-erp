using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain;

/// <summary>Línea de una RQ de la app de Almacenes (insumo para <see cref="Requisicion.CrearDesdeAlmacenesApp"/>).</summary>
public sealed record LineaAppRequisicion(
    Guid ArticuloId, decimal Cantidad, string UnidadMedida, Money PrecioEstimado,
    int? Piezas, int? AnchoMm, int? AltoMm,
    DateOnly? FechaRequerida = null, string? Obra = null);
