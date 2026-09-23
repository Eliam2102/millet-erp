using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Mailbox;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (ADR-0022) que polea el mailbox dedicado de CFDIs
/// (§9 del 01-diseno, §4 del 04-cuidados-infra). Cada tick (default 5
/// min, configurable en <see cref="WorkerSchedulingOptions"/>):
/// <list type="number">
///   <item>Lista mensajes pendientes con attachments en <c>Inbox</c>.</item>
///   <item>Para cada mensaje, detecta el primer attachment XML (UTF-8) y
///         opcionalmente un PDF acompañante, los descarga via
///         <see cref="IMailboxClient"/> y los pasa a
///         <see cref="IngresarCfdiCommand"/> con
///         <see cref="EmpresaIdOverride"/> resuelto desde
///         <see cref="MailboxRfcEmpresaMap"/> (RFC receptor del XML →
///         empresaId).</item>
///   <item>Mueve el mensaje a <c>Processed</c> si se ingresó o ya
///         existía (duplicado por UUID); a <c>Failed</c> si el XML está
///         malformado o no se encontró un RFC mapeado.</item>
/// </list>
///
/// <para>
/// **NoOp condicional**: si <see cref="MailboxOptions.IsConfigured"/>
/// es false, el client registrado es <c>NoOpMailboxClient</c> y el loop
/// loggea "skip tick" sin hacer trabajo (§4 — credenciales en Key Vault,
/// que en dev local no existen).
/// </para>
/// </summary>
public sealed class CfdiMailboxIngestionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<WorkerSchedulingOptions> _scheduling;
    private readonly IOptionsMonitor<MailboxOptions> _mailboxOptions;
    private readonly ILogger<CfdiMailboxIngestionWorker> _logger;

    public CfdiMailboxIngestionWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<WorkerSchedulingOptions> scheduling,
        IOptionsMonitor<MailboxOptions> mailboxOptions,
        ILogger<CfdiMailboxIngestionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _scheduling = scheduling;
        _mailboxOptions = mailboxOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _scheduling.CurrentValue;
        if (opts.Disabled)
        {
            _logger.LogInformation("[CfdiMailboxIngestionWorker] Disabled — loop no inicia.");
            return;
        }

        _logger.LogInformation(
            "[CfdiMailboxIngestionWorker] Iniciado. Interval={Interval}s.",
            opts.MailboxIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CfdiMailboxIngestionWorker] Error inesperado en tick. Continuando.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_scheduling.CurrentValue.MailboxIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Internal para que tests disparen un tick sin esperar el loop.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var mailboxOpts = _mailboxOptions.CurrentValue;
        if (!mailboxOpts.IsConfigured)
        {
            _logger.LogInformation("[CfdiMailboxIngestionWorker] Mailbox sin credenciales Graph — skip tick.");
            return;
        }

        var schedulingOpts = _scheduling.CurrentValue;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var mailbox = sp.GetRequiredService<IMailboxClient>();
        var mediator = sp.GetRequiredService<IMediator>();
        var parser = sp.GetRequiredService<IXmlCfdiParser>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        IReadOnlyList<MailboxMensaje> mensajes;
        try
        {
            mensajes = await mailbox.ListarPendientesAsync(mailboxOpts.MaxMensajesPorTick, cancellationToken);
        }
        catch (MailboxException ex)
        {
            _logger.LogError(ex,
                "[CfdiMailboxIngestionWorker] Listar pendientes falló ({Codigo}). Skip tick.",
                ex.Codigo);
            return;
        }

        if (mensajes.Count == 0)
        {
            _logger.LogDebug("[CfdiMailboxIngestionWorker] Nada pendiente en mailbox.");
            return;
        }

        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(CfdiMailboxIngestionWorker));
        using var bypass = empresaContext.Bypass();

        var ingresados = 0;
        var duplicados = 0;
        var fallidos = 0;
        foreach (var mensaje in mensajes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var (ok, dup, razon) = await ProcesarMensajeAsync(
                mailbox, mediator, parser, schedulingOpts.RfcsReceptores, mensaje, cancellationToken);

            if (ok) ingresados++;
            else if (dup) duplicados++;
            else
            {
                fallidos++;
                try
                {
                    await mailbox.MoverAFallidoAsync(mensaje.Id, razon ?? "DESCONOCIDO", cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[CfdiMailboxIngestionWorker] No se pudo mover mensaje {Mensaje} a Failed.",
                        mensaje.Id);
                }
            }
        }

        _logger.LogInformation(
            "[CfdiMailboxIngestionWorker] Tick: {Total} mensajes — ingresados={I}, duplicados={D}, fallidos={F}.",
            mensajes.Count, ingresados, duplicados, fallidos);
    }

    private async Task<(bool ok, bool duplicado, string? razon)> ProcesarMensajeAsync(
        IMailboxClient mailbox,
        IMediator mediator,
        IXmlCfdiParser parser,
        IDictionary<string, Guid> rfcMap,
        MailboxMensaje mensaje,
        CancellationToken cancellationToken)
    {
        var xmlAttachment = mensaje.Attachments.FirstOrDefault(IsXml);
        if (xmlAttachment is null)
        {
            _logger.LogWarning(
                "[CfdiMailboxIngestionWorker] Mensaje {Mensaje} from={From} sin attachment XML; mover a Failed.",
                mensaje.Id, mensaje.FromAddress);
            return (false, false, "SIN_XML");
        }

        byte[] xmlBytes;
        try
        {
            await using var xmlStream = await mailbox.DescargarAttachmentAsync(mensaje.Id, xmlAttachment.Id, cancellationToken);
            using var memory = new MemoryStream();
            await xmlStream.CopyToAsync(memory, cancellationToken);
            xmlBytes = memory.ToArray();
        }
        catch (MailboxException ex)
        {
            _logger.LogError(ex,
                "[CfdiMailboxIngestionWorker] Mensaje {Mensaje}: descarga del XML falló ({Codigo}).",
                mensaje.Id, ex.Codigo);
            return (false, false, ex.Codigo);
        }

        DatosCfdiParseados datos;
        try
        {
            using var parseStream = new MemoryStream(xmlBytes, writable: false);
            datos = parser.Parsear(parseStream);
        }
        catch (CfdiParseException ex)
        {
            _logger.LogWarning(ex,
                "[CfdiMailboxIngestionWorker] Mensaje {Mensaje} XML malformado ({Codigo}): {Mensaje2}",
                mensaje.Id, ex.Codigo, ex.Message);
            return (false, false, ex.Codigo);
        }

        if (!rfcMap.TryGetValue(datos.RfcReceptor.ToUpperInvariant(), out var empresaId))
        {
            _logger.LogWarning(
                "[CfdiMailboxIngestionWorker] Mensaje {Mensaje} RFC receptor {Rfc} no está mapeado en Workers:RfcsReceptores; mover a Failed.",
                mensaje.Id, datos.RfcReceptor);
            return (false, false, "RFC_RECEPTOR_NO_MAPEADO");
        }

        // PDF opcional: primer attachment con content-type application/pdf.
        Stream? pdfStream = null;
        try
        {
            var pdfAttachment = mensaje.Attachments.FirstOrDefault(IsPdf);
            if (pdfAttachment is not null)
            {
                pdfStream = await mailbox.DescargarAttachmentAsync(mensaje.Id, pdfAttachment.Id, cancellationToken);
            }

            using var ingestionStream = new MemoryStream(xmlBytes, writable: false);
            try
            {
                var resp = await mediator.Send(
                    new IngresarCfdiCommand(
                        Xml: ingestionStream,
                        Pdf: pdfStream,
                        Canal: CanalOrigenCfdi.Mailbox,
                        EmpresaIdOverride: empresaId),
                    cancellationToken);

                await mailbox.MoverAProcesadoAsync(mensaje.Id, cancellationToken);
                _logger.LogInformation(
                    "[CfdiMailboxIngestionWorker] UUID {Uuid} ingresado desde mensaje {Mensaje}.",
                    resp.UuidCfdi, mensaje.Id);
                return (true, false, null);
            }
            catch (BusinessRuleException ex) when (ex.Code == "CFDI_DUPLICADO")
            {
                await mailbox.MoverAProcesadoAsync(mensaje.Id, cancellationToken);
                _logger.LogInformation(
                    "[CfdiMailboxIngestionWorker] UUID duplicado en mensaje {Mensaje}; movido a Processed.",
                    mensaje.Id);
                return (false, true, null);
            }
        }
        finally
        {
            if (pdfStream is not null) await pdfStream.DisposeAsync();
        }
    }

    private static bool IsXml(MailboxAttachment a) =>
        a.Nombre.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        a.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsPdf(MailboxAttachment a) =>
        a.Nombre.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
        a.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
}
