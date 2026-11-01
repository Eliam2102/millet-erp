using Microsoft.Extensions.Logging;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Integraciones.Fiscal.Infrastructure.Avisos;

/// <summary>
/// Aviso de vencimiento del CSD solo al log (F6.9). El aviso dentro del ERP sale
/// del estado del CSD (banner); el correo llega con el servicio de avisos.
/// </summary>
// PLATFORM-TODO(86e3txv0n): enviar el aviso por correo con el servicio de avisos del ERP cuando se integre.
public sealed class LogAvisoCsdAdapter(ILogger<LogAvisoCsdAdapter> logger) : IAvisoCsdPort
{
    public Task EnviarAsync(AvisoCsd aviso, IReadOnlyList<DestinatarioAvisoCsd> destinatarios, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Aviso CSD (umbral {Umbral} días): el certificado {NumeroSerie} de la empresa {EmpresaId} vence el {VenceEn:yyyy-MM-dd} ({Dias} días). Destinatarios: {Destinatarios}.",
            aviso.UmbralDias, aviso.NumeroSerie, aviso.EmpresaId, aviso.VenceEn, aviso.DiasRestantes,
            string.Join(", ", destinatarios.Select(d => d.Email)));
        return Task.CompletedTask;
    }
}
