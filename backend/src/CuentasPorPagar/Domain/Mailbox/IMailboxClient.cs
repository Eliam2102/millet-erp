namespace Millet.CuentasPorPagar.Domain.Mailbox;

/// <summary>
/// Puerto del mailbox dedicado de CFDIs (§9.1 del 00-levantamiento,
/// §4.1 del 04-cuidados-infra). Cubre el ciclo: listar mensajes
/// pendientes → descargar attachments → marcar como procesado /
/// fallido sin retener PII.
///
/// <para>
/// Adapter real: <c>GraphMailboxClient</c> contra Microsoft Graph con
/// App Registration en Entra ID (cuentas con MFA no permiten IMAP/SMTP
/// user-password). Stub: <c>NoOpMailboxClient</c> cuando las
/// credenciales del Graph App no están configuradas.
/// </para>
/// </summary>
public interface IMailboxClient
{
    /// <summary>
    /// Lista los mensajes pendientes en el folder principal del mailbox
    /// (límite configurable). El orden devuelto es FIFO por fecha de
    /// recepción para que el worker procese los más viejos primero.
    /// </summary>
    Task<IReadOnlyList<MailboxMensaje>> ListarPendientesAsync(int max, CancellationToken cancellationToken);

    /// <summary>
    /// Descarga el contenido de un attachment por id. El stream
    /// retornado lo dispone el caller.
    /// </summary>
    Task<Stream> DescargarAttachmentAsync(string mensajeId, string attachmentId, CancellationToken cancellationToken);

    /// <summary>Mueve el mensaje a la carpeta <c>Processed</c>.</summary>
    Task MoverAProcesadoAsync(string mensajeId, CancellationToken cancellationToken);

    /// <summary>
    /// Mueve el mensaje a la carpeta <c>Failed</c> con un comentario
    /// como categoría (Microsoft Graph soporta categorías). Útil para
    /// que un humano revise por qué falló.
    /// </summary>
    Task MoverAFallidoAsync(string mensajeId, string razon, CancellationToken cancellationToken);
}

/// <summary>
/// Representación neutra de un mensaje del mailbox. Solo los datos que
/// el worker necesita; nada del cuerpo HTML / autores secundarios para
/// minimizar manejo de PII.
/// </summary>
public sealed record MailboxMensaje(
    string Id,
    string AsuntoTruncado,
    string FromAddress,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<MailboxAttachment> Attachments);

public sealed record MailboxAttachment(
    string Id,
    string Nombre,
    string ContentType,
    long? Tamanio);

/// <summary>Excepción del client de mailbox. El worker decide reintentar o archivar a Failed/.</summary>
public sealed class MailboxException : Exception
{
    public string Codigo { get; }

    public MailboxException(string codigo, string mensaje) : base(mensaje) { Codigo = codigo; }
    public MailboxException(string codigo, string mensaje, Exception inner) : base(mensaje, inner) { Codigo = codigo; }
}
