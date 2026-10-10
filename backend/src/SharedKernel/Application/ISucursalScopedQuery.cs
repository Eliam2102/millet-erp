namespace Millet.SharedKernel.Application;

public interface ISucursalScopedQuery
{
    string PermisoTodasSucursales { get; }
    IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}
