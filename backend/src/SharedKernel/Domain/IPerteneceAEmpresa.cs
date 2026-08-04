namespace Millet.SharedKernel.Domain;

/// <summary>
/// Marker para entidades que pertenecen a una empresa específica
/// (multi-tenant). El BaseDbContext aplica un global query filter automático
/// por <see cref="EmpresaId"/> y un interceptor asigna el valor en INSERT
/// desde el contexto del request.
/// Ver ADR-0011.
/// </summary>
public interface IPerteneceAEmpresa
{
    Guid EmpresaId { get; set; }
}
