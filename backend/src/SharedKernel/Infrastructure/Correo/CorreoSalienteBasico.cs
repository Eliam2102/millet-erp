using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application.Correo;

namespace Millet.SharedKernel.Infrastructure.Correo;

/// <summary>Sin transporte: no sale correo y el resultado es <see cref="EstadoCorreoSaliente.Simulado"/>.</summary>
public sealed class NoOpCorreoSaliente(ILogger<NoOpCorreoSaliente> logger) : ICorreoSaliente
{
    public bool EsSimulado => true;

    public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("[Correo] Envío simulado {Modulo}/{Tipo} con {Adjuntos} adjunto(s); no se entregó correo.",
            mensaje.Modulo, mensaje.Tipo, mensaje.AdjuntosOVacio.Count);
        return Task.FromResult(ResultadoCorreoSaliente.Simulado);
    }
}

/// <summary>
/// SMTP de captura en loopback (Mailpit). Solo se registra en Development;
/// nunca contacta un servidor de correo externo.
/// </summary>
public sealed class SmtpSandboxCorreoSaliente(IOptions<CorreoSalienteOptions> options) : ICorreoSaliente
{
    public bool EsSimulado => false;

    public async Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        var opciones = options.Value;
        using var correo = new MailMessage
        {
            From = new MailAddress(opciones.Smtp.Remitente, opciones.NombreRemitente),
            Subject = mensaje.Asunto,
            Body = mensaje.Html,
            IsBodyHtml = true,
        };
        foreach (var para in mensaje.Para) correo.To.Add(para);
        foreach (var adjunto in mensaje.AdjuntosOVacio)
            correo.Attachments.Add(new Attachment(
                new MemoryStream(adjunto.Contenido, writable: false), adjunto.Nombre, ContentTypeDe(adjunto)));

        using var smtp = new SmtpClient(opciones.Smtp.Host, opciones.Smtp.Puerto)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = false,
            UseDefaultCredentials = false,
        };
        try
        {
            await smtp.SendMailAsync(correo, cancellationToken);
            return ResultadoCorreoSaliente.Aceptado;
        }
        catch (SmtpException ex)
        {
            return ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.ErrorEnvio, $"SMTP local: {ex.Message}");
        }
    }

    internal static string ContentTypeDe(AdjuntoCorreo a) =>
        string.IsNullOrWhiteSpace(a.ContentType) ? MediaTypeNames.Application.Octet : a.ContentType;
}

/// <summary>
/// Transporte en memoria para pruebas: guarda cada mensaje y puede devolver
/// un error a pedido (<see cref="FallarCon"/>).
/// </summary>
public sealed class CorreoSalienteEnMemoria : ICorreoSaliente
{
    private readonly List<MensajeCorreo> _enviados = [];

    public bool EsSimulado { get; set; }

    /// <summary>Si no es null, cada envío devuelve este resultado y no se guarda.</summary>
    public ResultadoCorreoSaliente? FallarCon { get; set; }

    public IReadOnlyList<MensajeCorreo> Enviados
    {
        get { lock (_enviados) return [.. _enviados]; }
    }

    public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        if (FallarCon is { } error) return Task.FromResult(error);
        lock (_enviados) _enviados.Add(mensaje);
        return Task.FromResult(EsSimulado ? ResultadoCorreoSaliente.Simulado : ResultadoCorreoSaliente.Aceptado);
    }
}
