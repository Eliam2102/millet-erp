using System.Net.Mail;
using Microsoft.Extensions.Options;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;

namespace Millet.Api.Auth.Provisioning;

/// <summary>
/// Envía el correo de acceso a un buzón de captura SMTP en loopback.
/// Solo se registra en Development con proveedor Simulado o Graph y provisión activa.
/// Nunca contacta un servidor de correo externo.
/// </summary>
public sealed class CorreoSandboxLocal : ICorreoSalientePort
{
    private readonly CorreoSandboxOptions _options;
    private readonly bool _cuentaReal;

    public CorreoSandboxLocal(IOptions<EntraDirectorioOptions> options, IConfiguration configuration)
    {
        _options = options.Value.Simulacion.CorreoSandbox;
        _cuentaReal = string.Equals(configuration["Entra:Proveedor"], "Graph", StringComparison.OrdinalIgnoreCase);
    }

    public async Task EnviarAccesoColaboradorAsync(CorreoAccesoColaborador correo, CancellationToken ct)
    {
        using var mensaje = new MailMessage(
            "acceso@millet.local.test", correo.Destinatario,
            "[PRUEBA LOCAL] Acceso al ERP Millet",
            $"Hola {correo.NombreColaborador},\n\n" +
            (_cuentaReal
                ? $"Cuenta Microsoft de prueba: {correo.Upn}\nClave temporal: {correo.ContrasenaTemporal}\n\n" +
                  "La cuenta fue creada en el tenant de prueba. Microsoft exigirá cambiar la clave al ingresar.\n" +
                  "Este correo se capturó solo en el buzón local de QA; no llegó al destinatario externo.\n"
                : $"Cuenta simulada: {correo.Upn}\nClave temporal simulada: {correo.ContrasenaTemporal}\n\n" +
                  "Esta clave no sirve para iniciar sesión en Microsoft; no se creó una cuenta real.\n" +
                  "Para probar el acceso, un administrador debe usar 'Simular ingreso' en la cuenta del ERP local.\n") +
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
