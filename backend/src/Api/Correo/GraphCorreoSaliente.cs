using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Api.Correo;

/// <summary>
/// Correo saliente por Microsoft Graph desde el buzón del ERP (erpmillet@,
/// ADR-0063). Graph confirma que aceptó la solicitud, no la entrega en el
/// buzón. Los adjuntos van en el mismo correo hasta 3 MB en total.
/// </summary>
public sealed class GraphCorreoSaliente(IGraphSendMail graph, string remitente, string nombreRemitente) : ICorreoSaliente
{
    public bool EsSimulado => false;

    public async Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        var bytes = mensaje.AdjuntosOVacio.Sum(a => (long)a.Contenido.Length);
        if (bytes > CodigosCorreoSaliente.LimiteAdjuntosBytes)
            // PLATFORM-TODO(<CorreoEnlaceDescarga>): mandar un enlace de descarga cuando los adjuntos pasen de 3 MB.
            return ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.AdjuntosExceden3Mb,
                $"Los adjuntos suman {bytes / 1024d / 1024d:N1} MB; el correo admite hasta 3 MB.");

        try
        {
            await graph.EnviarAsync(remitente, Construir(mensaje, remitente, nombreRemitente), cancellationToken);
            return ResultadoCorreoSaliente.Aceptado;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403
            || string.Equals(ex.Error?.Code, "Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.Error?.Code, "ErrorAccessDenied", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.PermisosInsuficientes,
                "Microsoft Graph rechazó el envío por permisos insuficientes: la aplicación necesita Mail.Send sobre el buzón " +
                $"{remitente}. {ex.Error?.Message}".Trim());
        }
        catch (ODataError ex) when (EsLimitacion(ex))
        {
            return ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.LimiteProveedor,
                $"Exchange Online limitó el envío desde {remitente}: {ex.Error?.Message ?? ex.Message}");
        }
        catch (ODataError ex)
        {
            return ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.ErrorEnvio,
                $"Microsoft Graph: {ex.Error?.Message ?? ex.Message}");
        }
    }

    /// <summary>
    /// Limitación del buzón o de la organización (86e3zr74h): 429/503 de Graph,
    /// «550 5.7.233» (límite de destinatarios externos) y los códigos de cuota de
    /// Exchange. Se reintenta con espera; no es un rechazo definitivo.
    /// </summary>
    public static bool EsLimitacion(ODataError ex)
    {
        if (ex.ResponseStatusCode is 429 or 503) return true;
        var codigo = ex.Error?.Code ?? string.Empty;
        var texto = $"{ex.Error?.Message} {ex.Message}";
        return texto.Contains("5.7.233", StringComparison.Ordinal)
            || codigo.Equals("ErrorExceededMessageLimit", StringComparison.OrdinalIgnoreCase)
            || codigo.Equals("ErrorQuotaExceeded", StringComparison.OrdinalIgnoreCase)
            || codigo.Equals("ApplicationThrottled", StringComparison.OrdinalIgnoreCase)
            || codigo.Equals("MailboxConcurrency", StringComparison.OrdinalIgnoreCase)
            || codigo.Equals("TooManyRequests", StringComparison.OrdinalIgnoreCase);
    }

    public static SendMailPostRequestBody Construir(MensajeCorreo mensaje, string remitente, string nombreRemitente) => new()
    {
        Message = new Message
        {
            From = new Recipient { EmailAddress = new EmailAddress { Address = remitente, Name = nombreRemitente } },
            Subject = mensaje.Asunto,
            Body = new ItemBody { ContentType = BodyType.Html, Content = mensaje.Html },
            ToRecipients = [.. mensaje.Para.Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } })],
            Attachments = [.. mensaje.AdjuntosOVacio.Select(a => (Attachment)new FileAttachment
            {
                OdataType = "#microsoft.graph.fileAttachment",
                Name = a.Nombre,
                ContentType = a.ContentType,
                ContentBytes = a.Contenido,
            })],
        },
        SaveToSentItems = true,
    };
}

/// <summary>Costura sobre <c>Users[remitente].SendMail</c> para probar sin Graph.</summary>
public interface IGraphSendMail
{
    Task EnviarAsync(string remitente, SendMailPostRequestBody cuerpo, CancellationToken cancellationToken);
}

public sealed class GraphSendMail(GraphServiceClient graph) : IGraphSendMail
{
    public Task EnviarAsync(string remitente, SendMailPostRequestBody cuerpo, CancellationToken cancellationToken) =>
        graph.Users[remitente].SendMail.PostAsync(cuerpo, cancellationToken: cancellationToken);
}
