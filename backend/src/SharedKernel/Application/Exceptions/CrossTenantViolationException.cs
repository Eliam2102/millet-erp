using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Exceptions;

/// <summary>
/// Intento de leer, modificar o referenciar (FK) un registro de una
/// empresa distinta a la actual. Lanzada por el
/// <c>EmpresaContextSaveChangesInterceptor</c>. Mapea a HTTP 403.
/// Ver ADR-0011.
/// </summary>
public sealed class CrossTenantViolationException : DomainException
{
    public override string Code => "CROSS_EMPRESA_VIOLATION";

    public string EntityType { get; }

    public Guid CurrentEmpresaId { get; }

    public Guid TargetEmpresaId { get; }

    public CrossTenantViolationException(string entityType, Guid currentEmpresaId, Guid targetEmpresaId)
        : base($"Violación cross-empresa: no se puede acceder a '{entityType}' en empresa '{targetEmpresaId}' desde la empresa actual '{currentEmpresaId}'.")
    {
        EntityType = entityType;
        CurrentEmpresaId = currentEmpresaId;
        TargetEmpresaId = targetEmpresaId;
    }
}
