using Millet.ActivosFijos.Domain;
namespace Millet.ActivosFijos.Application;
public interface IConservacionReadPort
{
    Task ValidarAdjuntoAsync(Guid activo, DocumentoActivoDatos datos, CancellationToken ct);
    Task ValidarProveedorAsync(Guid proveedor, CancellationToken ct);
    Task<string> EmpresaAsync(Guid empresa, CancellationToken ct);
}
