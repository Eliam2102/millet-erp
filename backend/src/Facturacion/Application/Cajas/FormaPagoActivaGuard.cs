using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas;

public static class FormaPagoActivaGuard
{
    public static async Task VerificarAsync(string clave, ICatalogosSatReadPort catalogos, CancellationToken ct)
    {
        if (!await catalogos.ExisteFormaPagoAsync(clave, ct))
            throw new BusinessRuleException("FORMA_PAGO_INVALIDA",
                $"La forma de pago '{clave}' no existe o está desactivada. Selecciona una forma de pago SAT activa.");
    }
}
