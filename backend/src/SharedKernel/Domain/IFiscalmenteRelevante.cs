namespace Millet.SharedKernel.Domain;

/// <summary>
/// Marker para entidades con implicaciones fiscales (CFDIs, asientos
/// contables, pagos, notas de crédito). Estas entidades NUNCA se borran
/// físicamente: el repositorio expone soft delete (marca
/// <see cref="BaseEntity.DeletedAt"/>) y el global query filter del
/// BaseDbContext excluye automáticamente las que tienen <c>deleted_at != NULL</c>.
/// Ver ADR-0008.
/// </summary>
public interface IFiscalmenteRelevante
{
}
