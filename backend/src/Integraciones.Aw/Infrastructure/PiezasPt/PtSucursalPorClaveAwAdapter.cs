using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.ProductoTerminado.Application.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.PiezasPt;

/// <summary>Puerto de PT sobre el resolvedor de sucursales por clave A+W de Facturación (clave vacía → null).</summary>
public sealed class PtSucursalPorClaveAwAdapter(ISucursalPorClaveAwResolver resolver) : IPtSucursalPorClaveAwPort
{
    public Task<Guid?> ResolverAsync(string claveAw, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(claveAw) ? Task.FromResult<Guid?>(null) : resolver.ResolverAsync(claveAw, ct);
}
