using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Domain.Ports.Correo;

namespace Millet.CuentasPorCobrar.Infrastructure.Adapters;

/// <summary>
/// Configuración del correo del estado de cuenta (C7.8). Sección
/// <c>CuentasPorCobrar:EstadoCuentaCorreo</c>. <c>Proveedor</c>:
/// <c>NoOp</c> (default) o <c>SandboxLocal</c> (solo Development, SMTP de
/// captura en loopback, p. ej. Mailpit).
/// </summary>
public sealed class EstadoCuentaCorreoOptions
{
    public const string SectionName = "CuentasPorCobrar:EstadoCuentaCorreo";

    public string Proveedor { get; set; } = "NoOp";
    public string Host { get; set; } = "127.0.0.1";
    public int Puerto { get; set; } = 1025;
    public string Remitente { get; set; } = "cobranza@millet.local.test";

    public bool EsSandboxLocal => Proveedor.Equals("SandboxLocal", StringComparison.OrdinalIgnoreCase);

    public bool EsLoopback => Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || Host == "127.0.0.1" || Host == "::1";
}

/// <summary>
/// Sin transporte de correo: el envío queda en estado Registrado (el PDF
/// ya se descargó en el navegador y el usuario lo hace llegar por su canal).
/// </summary>
// PLATFORM-TODO(<EnvioEstadoCuentaCliente>): reemplazar por el adaptador del motor de correo transversal (Graph/SMTP corporativo, ADR-0026) cuando exista; hasta entonces producción solo registra el envío.
public sealed class NoOpEstadoCuentaCorreoAdapter(ILogger<NoOpEstadoCuentaCorreoAdapter> logger) : IEstadoCuentaCorreoPort
{
    public Task<bool> EnviarAsync(CorreoEstadoCuenta correo, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[EstadoCuentaCorreo] Sin transporte de correo configurado; el envío del estado de cuenta queda registrado ({Archivo}).",
            correo.NombreArchivo);
        return Task.FromResult(false);
    }
}

/// <summary>
/// Envía el estado de cuenta a un buzón de captura SMTP en loopback
/// (Mailpit). Solo se registra en Development; nunca contacta un servidor
/// de correo externo (mismo criterio que <c>CorreoSandboxLocal</c>).
/// </summary>
public sealed class SandboxLocalEstadoCuentaCorreoAdapter(
    IOptions<EstadoCuentaCorreoOptions> options,
    ILogger<SandboxLocalEstadoCuentaCorreoAdapter> logger) : IEstadoCuentaCorreoPort
{
    public async Task<bool> EnviarAsync(CorreoEstadoCuenta correo, CancellationToken cancellationToken)
    {
        var opciones = options.Value;
        using var mensaje = new MailMessage(opciones.Remitente, correo.Destinatario)
        {
            Subject = $"Estado de cuenta — {correo.RazonSocialCliente}",
            Body = $"Estimado cliente {correo.RazonSocialCliente}:\n\n" +
                   "Adjuntamos su estado de cuenta con los movimientos aplicados a la fecha de emisión.\n" +
                   (string.IsNullOrWhiteSpace(correo.Mensaje) ? "" : $"\n{correo.Mensaje}\n") +
                   "\nSi tiene alguna aclaración, responda a este correo.\n\nCobranza Millet\n\n" +
                   "[PRUEBA LOCAL] Este correo se capturó en el buzón local de QA; no llegó al destinatario externo.\n",
        };
        using var pdf = new MemoryStream(correo.Pdf, writable: false);
        mensaje.Attachments.Add(new Attachment(pdf, correo.NombreArchivo, "application/pdf"));

        using var smtp = new SmtpClient(opciones.Host, opciones.Puerto)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = false,
            UseDefaultCredentials = false,
        };
        await smtp.SendMailAsync(mensaje, cancellationToken);
        logger.LogInformation("[EstadoCuentaCorreo] Estado de cuenta entregado al SMTP local {Host}:{Puerto}.",
            opciones.Host, opciones.Puerto);
        return true;
    }
}
