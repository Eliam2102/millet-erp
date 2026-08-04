using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Millet.CuentasPorPagar.Domain.Mailbox;

namespace Millet.CuentasPorPagar.Infrastructure.Mailbox;

/// <summary>
/// Adapter de <see cref="IMailboxClient"/> contra Microsoft Graph
/// (F2-PR2). Usa App Registration en Entra ID con client credentials
/// (<see cref="ClientSecretCredential"/>); el App tiene asignado el
/// permiso <c>Mail.ReadWrite</c> sobre el mailbox de servicio.
///
/// <para>
/// **Resolución de folders:** los nombres de folders configurados
/// (<c>Inbox/Processed/Failed</c>) se resuelven a IDs vía
/// <c>GET /users/{upn}/mailFolders</c> al primer uso y se cachean en
/// memoria del cliente.
/// </para>
///
/// <para>
/// **PII y logging:** este client nunca loggea cuerpo del mensaje;
/// solo el asunto truncado a 80 chars + From + ReceivedAt + filename
/// de attachments. Suficiente para debugging sin filtrar datos.
/// </para>
/// </summary>
public sealed class GraphMailboxClient : IMailboxClient, IDisposable
{
    private readonly MailboxOptions _options;
    private readonly GraphServiceClient _graph;
    private readonly ILogger<GraphMailboxClient> _logger;

    private string? _inboxFolderId;
    private string? _processedFolderId;
    private string? _failedFolderId;
    private bool _disposed;

    public GraphMailboxClient(IOptions<MailboxOptions> options, ILogger<GraphMailboxClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        var credential = new ClientSecretCredential(
            _options.TenantId, _options.ClientId, _options.ClientSecret);
        _graph = new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _graph.Dispose();
        _disposed = true;
    }

    public async Task<IReadOnlyList<MailboxMensaje>> ListarPendientesAsync(int max, CancellationToken cancellationToken)
    {
        try
        {
            var inboxId = await ResolveFolderIdAsync(_options.InboxFolderName, cancellationToken);

            var messages = await _graph
                .Users[_options.MailboxUpn]
                .MailFolders[inboxId]
                .Messages
                .GetAsync(req =>
                {
                    req.QueryParameters.Top = Math.Min(Math.Max(max, 1), 100);
                    req.QueryParameters.Orderby = ["receivedDateTime asc"];
                    req.QueryParameters.Select = ["id", "subject", "from", "receivedDateTime", "hasAttachments"];
                }, cancellationToken);

            var result = new List<MailboxMensaje>();
            foreach (var msg in messages?.Value ?? [])
            {
                if (msg.Id is null) continue;
                if (!(msg.HasAttachments ?? false)) continue;

                var attachments = await _graph
                    .Users[_options.MailboxUpn]
                    .Messages[msg.Id]
                    .Attachments
                    .GetAsync(req => req.QueryParameters.Select = ["id", "name", "contentType", "size"], cancellationToken);

                var attachList = (attachments?.Value ?? [])
                    .Where(a => a is FileAttachment && !string.IsNullOrEmpty(a.Id))
                    .Select(a => new MailboxAttachment(
                        a.Id!,
                        a.Name ?? "(sin nombre)",
                        a.ContentType ?? "application/octet-stream",
                        a.Size.HasValue ? a.Size.Value : null))
                    .ToList();

                if (attachList.Count == 0) continue;

                result.Add(new MailboxMensaje(
                    Id: msg.Id,
                    AsuntoTruncado: TruncarAsunto(msg.Subject),
                    FromAddress: msg.From?.EmailAddress?.Address ?? "(desconocido)",
                    ReceivedAt: msg.ReceivedDateTime ?? DateTimeOffset.UtcNow,
                    Attachments: attachList));
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MailboxException("MAILBOX_LIST_FALLO", $"No se pudo listar mensajes: {ex.Message}", ex);
        }
    }

    public async Task<Stream> DescargarAttachmentAsync(string mensajeId, string attachmentId, CancellationToken cancellationToken)
    {
        try
        {
            var attachment = await _graph
                .Users[_options.MailboxUpn]
                .Messages[mensajeId]
                .Attachments[attachmentId]
                .GetAsync(cancellationToken: cancellationToken);

            if (attachment is FileAttachment fileAttachment && fileAttachment.ContentBytes is { } bytes)
            {
                return new MemoryStream(bytes, writable: false);
            }

            throw new MailboxException(
                "MAILBOX_ATTACHMENT_VACIO",
                $"El attachment {attachmentId} del mensaje {mensajeId} no es FileAttachment o está vacío.");
        }
        catch (MailboxException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MailboxException("MAILBOX_DESCARGA_FALLO",
                $"No se pudo descargar attachment {attachmentId}: {ex.Message}", ex);
        }
    }

    public async Task MoverAProcesadoAsync(string mensajeId, CancellationToken cancellationToken)
    {
        var destino = await ResolveFolderIdAsync(_options.ProcessedFolderName, cancellationToken);
        await MoverAsync(mensajeId, destino, cancellationToken);
    }

    public async Task MoverAFallidoAsync(string mensajeId, string razon, CancellationToken cancellationToken)
    {
        var destino = await ResolveFolderIdAsync(_options.FailedFolderName, cancellationToken);
        try
        {
            await _graph
                .Users[_options.MailboxUpn]
                .Messages[mensajeId]
                .PatchAsync(new Message
                {
                    Categories = [$"CxP:Failed:{TruncarCategoria(razon)}"],
                }, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[GraphMailboxClient] No se pudo etiquetar mensaje {Mensaje} con categoría de fallo; se mueve igual.",
                mensajeId);
        }
        await MoverAsync(mensajeId, destino, cancellationToken);
    }

    private async Task MoverAsync(string mensajeId, string folderId, CancellationToken cancellationToken)
    {
        try
        {
            await _graph
                .Users[_options.MailboxUpn]
                .Messages[mensajeId]
                .Move
                .PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.Move.MovePostRequestBody
                {
                    DestinationId = folderId,
                }, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MailboxException("MAILBOX_MOVE_FALLO",
                $"No se pudo mover mensaje {mensajeId} a folder {folderId}: {ex.Message}", ex);
        }
    }

    private async Task<string> ResolveFolderIdAsync(string folderName, CancellationToken cancellationToken)
    {
        if (string.Equals(folderName, _options.InboxFolderName, StringComparison.OrdinalIgnoreCase) && _inboxFolderId is not null)
            return _inboxFolderId;
        if (string.Equals(folderName, _options.ProcessedFolderName, StringComparison.OrdinalIgnoreCase) && _processedFolderId is not null)
            return _processedFolderId;
        if (string.Equals(folderName, _options.FailedFolderName, StringComparison.OrdinalIgnoreCase) && _failedFolderId is not null)
            return _failedFolderId;

        var folders = await _graph
            .Users[_options.MailboxUpn]
            .MailFolders
            .GetAsync(req => req.QueryParameters.Filter = $"displayName eq '{folderName}'", cancellationToken);

        var folder = folders?.Value?.FirstOrDefault()
            ?? throw new MailboxException(
                "MAILBOX_FOLDER_NO_ENCONTRADO",
                $"No se encontró la carpeta '{folderName}' en el mailbox {_options.MailboxUpn}.");

        var id = folder.Id
            ?? throw new MailboxException("MAILBOX_FOLDER_SIN_ID", $"La carpeta '{folderName}' no tiene id.");

        if (string.Equals(folderName, _options.InboxFolderName, StringComparison.OrdinalIgnoreCase))
            _inboxFolderId = id;
        else if (string.Equals(folderName, _options.ProcessedFolderName, StringComparison.OrdinalIgnoreCase))
            _processedFolderId = id;
        else if (string.Equals(folderName, _options.FailedFolderName, StringComparison.OrdinalIgnoreCase))
            _failedFolderId = id;

        return id;
    }

    private static string TruncarAsunto(string? asunto)
    {
        if (string.IsNullOrEmpty(asunto)) return "(sin asunto)";
        return asunto.Length <= 80 ? asunto : asunto[..80];
    }

    private static string TruncarCategoria(string razon)
    {
        if (string.IsNullOrEmpty(razon)) return "DESCONOCIDO";
        return razon.Length <= 60 ? razon : razon[..60];
    }
}
