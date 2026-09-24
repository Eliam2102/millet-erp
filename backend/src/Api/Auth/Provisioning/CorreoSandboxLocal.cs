using System.Net.Mail;
using Microsoft.Extensions.Options;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;

namespace Millet.Api.Auth.Provisioning;

/// <summary>
/// Envía el correo de acceso a un buzón de captura SMTP en loopback.
/// Solo se registra en Development con proveedor Simulado y provisión activa.
/// Nunca contacta un servidor de correo externo.
/// </summary>
public sealed class CorreoSandboxLocal : ICorreoSalientePort
{
    private readonly CorreoSandboxOptions _options;

    public CorreoSandboxLocal(IOptions<EntraDirectorioOptions> options)
    {
        _options = options.Value.Simulacion.CorreoSandbox;
    }

    public async Task EnviarAccesoColaboradorAsync(CorreoAccesoColaborador correo, CancellationToken ct)
    {
        using var mensaje = new MailMessage(
            "acceso@millet.local.test", correo.Destinatario,
            "[PRUEBA LOCAL] Acceso al ERP Millet",
            $"Hola {correo.NombreColaborador},\n\n" +
            $"Cuenta simulada: {correo.Upn}\n" +
            $"Clave temporal simulada: {correo.ContrasenaTemporal}\n\n" +
            "Esta clave no sirve para iniciar sesión en Microsoft; no se creó una cuenta real.\n" +
            "Para probar el acceso, un administrador debe usar 'Simular ingreso' en la cuenta del ERP local.\n" +
            $"ERP local: {correo.UrlInicioSesion}\n");
        using var smtp = new SmtpClient(_options.Host, _options.Puerto)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = false,
            UseDefaultCredentials = false
        };
        await smtp.SendMailAsync(mensaje, ct);
    }
}
