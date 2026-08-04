using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Mailbox;

namespace Millet.CuentasPorPagar.Infrastructure.Mailbox;

/// <summary>
/// Stub de <see cref="IMailboxClient"/> (F2-PR2) que devuelve lista
/// vacía y nunca falla. Activo cuando
/// <see cref="MailboxOptions.IsConfigured"/> es false — dev local sin
/// App Registration y QA/Prod si todavía no se provisionó.
///
/// PLATFORM-TODO(&lt;MailboxIngestion&gt;): cuando el adapter Graph
/// esté wireado en producción, este stub se conserva para test
/// fixtures.
/// </summary>
public sealed class NoOpMailboxClient : IMailboxClient
{
    private readonly ILogger<NoOpMailboxClient> _logger;

    public NoOpMailboxClient(ILogger<NoOpMailboxClient> logger) { _logger = logger; }

    public Task<IReadOnlyList<MailboxMensaje>> ListarPendientesAsync(int max, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpMailboxClient] ListarPendientes max={Max} → 0 mensajes (stub).", max);
        return Task.FromResult<IReadOnlyList<MailboxMensaje>>([]);
    }

    public Task<Stream> DescargarAttachmentAsync(string mensajeId, string attachmentId, CancellationToken cancellationToken)
    {
        // Nunca debería llamarse en producción con el stub porque
        // ListarPendientes devuelve vacío. Si pasa (test mal construido),
        // se loggea y se devuelve un stream vacío.
        _logger.LogWarning(
            "[NoOpMailboxClient] DescargarAttachment llamado con stub: mensaje={Mensaje} attachment={Attachment}",
            mensajeId, attachmentId);
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task MoverAProcesadoAsync(string mensajeId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpMailboxClient] MoverAProcesado mensaje={Mensaje} (no-op).", mensajeId);
        return Task.CompletedTask;
    }

    public Task MoverAFallidoAsync(string mensajeId, string razon, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpMailboxClient] MoverAFallido mensaje={Mensaje} razon={Razon} (no-op).", mensajeId, razon);
        return Task.CompletedTask;
    }
}
