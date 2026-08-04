using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Exceptions;

/// <summary>
/// Lanzada cuando un INSERT o UPDATE sobre una entidad <c>IPerteneceAEmpresa</c>
/// se ejecuta sin un EmpresaContext.Current válido y sin estar dentro de un
/// Bypass scope. Indica un error de programación: cualquier operación
/// multi-tenant requiere contexto de empresa explícito. Mapea a HTTP 500
/// (es un error interno, no una violación de regla del usuario).
/// Ver ADR-0011.
/// </summary>
public sealed class MissingEmpresaContextException : DomainException
{
    public override string Code => "MISSING_EMPRESA_CONTEXT";

    public string EntityType { get; }

    public MissingEmpresaContextException(string entityType)
        : base($"No se puede operar sobre la entidad '{entityType}' (IPerteneceAEmpresa) sin un EmpresaContext.Current. Si la operación es legítima fuera de un request (test, migración, seed), envuélvela en un using del método Bypass de ICurrentEmpresaContext.")
    {
        EntityType = entityType;
    }
}
