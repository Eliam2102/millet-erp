using Millet.CuentasPorPagar.Domain.Mailbox;

namespace Millet.CuentasPorPagar.Infrastructure.Mailbox;

/// <summary>
/// Buzón simulado en disco (P8.1) para dev y pruebas: nunca toca Graph.
/// Layout: <c>{raíz}/Inbox/{mensaje}/{adjuntos}</c>; "mover" cambia la carpeta
/// del mensaje a <c>Processed</c> o <c>Failed</c>. Un archivo opcional
/// <c>_de.txt</c> en la carpeta del mensaje fija el remitente.
/// ponytail: sin filtro de entorno; solo se activa con MailboxOptions.SimuladorDirectorio (vacío en QA/Prod).
/// </summary>
public sealed class DirectorioMailboxClient : IMailboxClient
{
    private readonly string _raiz;
    private readonly MailboxOptions _opts;

    public DirectorioMailboxClient(Microsoft.Extensions.Options.IOptions<MailboxOptions> options)
    {
        _opts = options.Value;
        _raiz = Path.GetFullPath(_opts.SimuladorDirectorio);
    }

    public Task<IReadOnlyList<MailboxMensaje>> ListarPendientesAsync(int max, CancellationToken cancellationToken)
    {
        var inbox = Path.Combine(_raiz, _opts.InboxFolderName);
        Directory.CreateDirectory(inbox);
        IReadOnlyList<MailboxMensaje> lista = new DirectoryInfo(inbox).GetDirectories()
            .OrderBy(d => d.CreationTimeUtc).ThenBy(d => d.Name, StringComparer.Ordinal)
            .Take(Math.Max(max, 1))
            .Select(d => new MailboxMensaje(
                d.Name,
                d.Name.Length > 80 ? d.Name[..80] : d.Name,
                LeerRemitente(d),
                new DateTimeOffset(d.CreationTimeUtc, TimeSpan.Zero),
                d.GetFiles().Where(f => f.Name != "_de.txt").OrderBy(f => f.Name, StringComparer.Ordinal)
                    .Select(f => new MailboxAttachment(f.Name, f.Name, TipoMime(f.Name), f.Length)).ToList()))
            .ToList();
        return Task.FromResult(lista);
    }

    public Task<Stream> DescargarAttachmentAsync(string mensajeId, string attachmentId, CancellationToken cancellationToken)
    {
        var ruta = Ruta(_opts.InboxFolderName, mensajeId, attachmentId);
        if (!File.Exists(ruta))
            throw new MailboxException("MAILBOX_ATTACHMENT_VACIO", $"No existe el adjunto {attachmentId} del mensaje {mensajeId}.");
        return Task.FromResult<Stream>(new MemoryStream(File.ReadAllBytes(ruta), writable: false));
    }

    public Task MoverAProcesadoAsync(string mensajeId, CancellationToken cancellationToken) =>
        Mover(mensajeId, _opts.ProcessedFolderName);

    public Task MoverAFallidoAsync(string mensajeId, string razon, CancellationToken cancellationToken) =>
        Mover(mensajeId, _opts.FailedFolderName);

    private Task Mover(string mensajeId, string destino)
    {
        var origen = Ruta(_opts.InboxFolderName, mensajeId);
        if (!Directory.Exists(origen)) return Task.CompletedTask; // ya movido: idempotente
        var dir = Path.Combine(_raiz, destino);
        Directory.CreateDirectory(dir);
        var final = Path.Combine(dir, mensajeId);
        if (Directory.Exists(final)) final += "-" + Guid.NewGuid().ToString("N")[..6];
        Directory.Move(origen, final);
        return Task.CompletedTask;
    }

    // Los ids vienen del propio listado; aun así se valida que no escapen de la raíz.
    private string Ruta(params string[] partes)
    {
        var ruta = Path.GetFullPath(Path.Combine([_raiz, .. partes]));
        if (!ruta.StartsWith(_raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new MailboxException("MAILBOX_RUTA_INVALIDA", "Ruta fuera del buzón simulado.");
        return ruta;
    }

    private static string LeerRemitente(DirectoryInfo d)
    {
        var f = Path.Combine(d.FullName, "_de.txt");
        return File.Exists(f) ? File.ReadAllText(f).Trim() : "simulador@local";
    }

    private static string TipoMime(string nombre) => Path.GetExtension(nombre).ToLowerInvariant() switch
    {
        ".xml" => "application/xml",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}
