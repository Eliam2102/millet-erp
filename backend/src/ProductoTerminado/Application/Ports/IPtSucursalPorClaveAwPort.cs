namespace Millet.ProductoTerminado.Application.Ports;

/// <summary>Resuelve la sucursal del ERP a partir de la clave de sucursal de A+W (implementa Integraciones.Aw).</summary>
public interface IPtSucursalPorClaveAwPort
{
    Task<Guid?> ResolverAsync(string claveAw, CancellationToken ct);
}
