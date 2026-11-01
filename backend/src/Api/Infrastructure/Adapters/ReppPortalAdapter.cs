using MediatR;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.Tesoreria.Application.Repp;
namespace Millet.Api.Infrastructure.Adapters;

/// <summary>P8.3: el complemento del portal se registra en Tesorería (mismo registro y evento que el manual); un fallo inesperado no tira la subida.</summary>
public sealed class ReppPortalAdapter(ISender sender, ILogger<ReppPortalAdapter> logger) : IReppPortalPort
{
    public async Task<ReppPortalLigado> LigarAsync(Guid proveedorId, byte[] xml, CancellationToken cancellationToken)
    {
        try
        {
            var r = await sender.Send(new RegistrarReppPortalCommand(proveedorId, xml), cancellationToken);
            return new(r.Ligado, r.Mensaje);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[PortalProveedor] No se pudo ligar el complemento del proveedor {ProveedorId}.", proveedorId);
            return new(false, "No pudimos ligarlo a tu pago. Cuentas por Pagar lo revisará.");
        }
    }
}
