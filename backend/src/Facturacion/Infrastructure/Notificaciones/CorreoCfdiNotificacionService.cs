using System.Net;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Facturacion.Infrastructure.Notificaciones;

/// <summary>
/// Entrega el CFDI al cliente (PDF y XML) por el transporte único de correo
/// (86e3txv0n). Un rechazo del proveedor se lanza con su código y motivo para
/// que <c>EnvioCfdiCorreoWorker</c> deje la bitácora en Fallido y reintente;
/// con el proveedor NoOp el envío se da por aceptado como hasta ahora.
/// </summary>
public sealed class CorreoCfdiNotificacionService(ICorreoSaliente correo) : INotificacionService
{
    public const string Modulo = "facturacion";
    public const string Tipo = "cfdi-cliente";

    public async Task<bool> EnviarCfdiPorCorreoAsync(EnvioCfdiCorreo envio, CancellationToken cancellationToken)
    {
        var resultado = await correo.EnviarAsync(Construir(envio), cancellationToken);
        if (resultado.Estado == EstadoCorreoSaliente.Error)
            throw new InvalidOperationException($"{resultado.Codigo}: {resultado.Motivo}");
        return true;
    }

    public static MensajeCorreo Construir(EnvioCfdiCorreo envio)
    {
        var nombre = string.IsNullOrWhiteSpace(envio.Uuid) ? envio.Folio : $"{envio.Folio}_{envio.Uuid}";
        var adjuntos = new List<AdjuntoCorreo>();
        if (envio.PdfAdjunto is { Length: > 0 } pdf)
            adjuntos.Add(new AdjuntoCorreo($"{nombre}.pdf", "application/pdf", pdf));
        if (!string.IsNullOrEmpty(envio.XmlAdjunto))
            adjuntos.Add(new AdjuntoCorreo($"{nombre}.xml", "application/xml", System.Text.Encoding.UTF8.GetBytes(envio.XmlAdjunto)));

        var folio = WebUtility.HtmlEncode(envio.Folio);
        var uuid = WebUtility.HtmlEncode(envio.Uuid ?? "sin folio fiscal");
        var html = "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
                   $"<h2>Factura {folio}</h2>" +
                   $"<p>Adjuntamos el PDF y el XML de su factura <strong>{folio}</strong> (folio fiscal {uuid}).</p>" +
                   "<p>Si tiene alguna aclaración, responda a este correo.</p><p>Millet</p></div>";
        return new MensajeCorreo(envio.EmpresaId, Modulo, Tipo, envio.ComprobanteId, [envio.Destinatario],
            $"Factura {envio.Folio} de Millet", html, adjuntos);
    }
}
