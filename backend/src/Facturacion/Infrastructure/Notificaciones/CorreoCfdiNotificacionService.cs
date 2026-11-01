using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Correo;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Notificaciones;

/// <summary>
/// PLATFORM-TODO(&lt;EnvioCfdiCliente&gt;): stub del servicio de notificación.
/// Loggea el envío y lo da por aceptado (no entrega correo real). Se reemplaza
/// por el adapter a <c>Millet.Integraciones.Mailbox</c> / SMTP / Microsoft Graph
/// cuando exista (§12.5 levantamiento).
/// </summary>
public sealed class NoOpNotificacionService : INotificacionService, ICorreoDocumentoService
{
    public bool EsSimulado => true;

    // PLATFORM-TODO(<V08>): conectar el transporte corporativo cuando se confirme el remitente del tenant.
    public Task EnviarAsync(CorreoDocumento correo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Envío simulado de documento {Archivo}, {Bytes} bytes. No se entregó correo.",
            correo.NombreArchivo, correo.Pdf.Length);
        return Task.CompletedTask;
    }

    private readonly ILogger<NoOpNotificacionService> _logger;

    public NoOpNotificacionService(ILogger<NoOpNotificacionService> logger) => _logger = logger;

    public Task<bool> EnviarCfdiPorCorreoAsync(EnvioCfdiCorreo envio, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[NoOpNotificacionService] ENVÍO FAKE de CFDI folio={Folio} uuid={Uuid} → {Destinatario} " +
            "(pdf={PdfBytes}B, xml={TieneXml}) (stub F2-PR2, PLATFORM-TODO<EnvioCfdiCliente>)",
            envio.Folio, envio.Uuid, envio.Destinatario,
            envio.PdfAdjunto?.Length ?? 0, envio.XmlAdjunto is not null);
        return Task.FromResult(true);
    }
}
