using Millet.Identidad.Application.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Infrastructure.Stubs;

/// <summary>
/// Adaptador seguro por defecto: no acepta un envío que nadie recibirá.
/// Las pruebas de integración inyectan su propio correo controlado.
/// </summary>
public sealed class CorreoSalienteSimulado : ICorreoSalientePort
{
    // PLATFORM-TODO(<CorreoSaliente>): reemplazar por el adaptador de Graph
    // (Mail.Send desde el buzón de servicio) cuando TI lo entregue (plan 15 §8).

    public Task EnviarAccesoColaboradorAsync(CorreoAccesoColaborador correo, CancellationToken ct)
    {
        throw new BusinessRuleException(
            "CORREO_ACCESO_NO_CONFIGURADO",
            "No hay un proveedor de correo real configurado para entregar el acceso.");
    }
}
