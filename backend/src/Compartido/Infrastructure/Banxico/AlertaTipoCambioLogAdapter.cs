using Microsoft.Extensions.Logging;
using Millet.Compartido.Application.Ports;

namespace Millet.Compartido.Infrastructure.Banxico;

/// <summary>Deja el aviso del tipo de cambio en el log mientras no hay servicio de correo.</summary>
public sealed class AlertaTipoCambioLogAdapter(ILogger<AlertaTipoCambioLogAdapter> logger) : IAlertaTipoCambioPort
{
    // PLATFORM-TODO(86e3txv0n): enviar el aviso por el servicio de correo (core.correo_envio)
    // a los usuarios con el rol del aviso cuando exista.
    public Task NotificarAsync(AlertaTipoCambio alerta, CancellationToken cancellationToken)
    {
        logger.LogWarning("Aviso de tipo de cambio para {Rol}: {Asunto}. {Detalle}", alerta.Rol, alerta.Asunto, alerta.Detalle);
        return Task.CompletedTask;
    }
}
