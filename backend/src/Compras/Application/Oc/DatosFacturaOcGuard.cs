using Millet.Compras.Domain.Ports.Catalogos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc;

public static class DatosFacturaOcGuard
{
    public static async Task ValidarAsync(ICatalogoSatReadPort catalogos, string? uso, string? forma, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(uso) && (await catalogos.ObtenerUsoCfdiAsync(uso, ct))?.Activa != true)
            throw new BusinessRuleException("OC_USO_CFDI_INVALIDO", "Selecciona un uso de CFDI existente y activo del catálogo SAT.");
        if (!string.IsNullOrEmpty(forma) && (await catalogos.ObtenerFormaPagoAsync(forma, ct))?.Activa != true)
            throw new BusinessRuleException("OC_FORMA_PAGO_INVALIDA", "Selecciona una forma de pago existente y activa del catálogo SAT.");
    }
}
