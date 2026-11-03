using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Infrastructure.Workers;

/// <summary>
/// Worker en-proceso (ADR-0022) que polea el mailbox dedicado de CFDIs
/// (§9 del 01-diseno, §4 del 04-cuidados-infra). Cada tick (default 15
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

    private enum Resultado { Ingresado, Duplicado, Revision, Reintentar }

    private sealed record Salida(Resultado Resultado, string? Razon = null, Guid? EmpresaId = null);

    /// <summary>Internal para que tests disparen un tick sin esperar el loop.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        var mailboxOpts = _mailboxOptions.CurrentValue;
        if (!mailboxOpts.IsActivo)
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
        var db = sp.GetRequiredService<CuentasPorPagarDbContext>();
        var clock = sp.GetRequiredService<IClock>();

        var inicio = clock.UtcNow;
        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(CfdiMailboxIngestionWorker));
        using var bypass = empresaContext.Bypass();

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
            await RegistrarCorridaAsync(db, BuzonEjecucion.Registrar(inicio, clock.UtcNow, 0, 0, 0, 0, ex.Codigo), cancellationToken);
            return;
        }

        if (mensajes.Count == 0)
        {
            _logger.LogDebug("[CfdiMailboxIngestionWorker] Nada pendiente en mailbox.");
            await RegistrarCorridaAsync(db, BuzonEjecucion.Registrar(inicio, clock.UtcNow, 0, 0, 0, 0, null), cancellationToken);
            return;
        }

        var ingresados = 0;
        var duplicados = 0;
        var revision = 0;
        foreach (var mensaje in mensajes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            Salida salida;
            try
            {
                salida = await ProcesarMensajeAsync(
                    mailbox, mediator, parser, schedulingOpts.RfcsReceptores, mailboxOpts.MaxXmlBytes, mensaje, cancellationToken);
            }
            catch (BusinessRuleException ex)
            {
                // CFDI con UUID/RFC inválido u otra regla del handler: revisión humana, no tumba el tick.
                db.ChangeTracker.Clear();
                salida = new Salida(Resultado.Revision, ex.Code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Falla técnica (BD, red): el correo se queda en Inbox y se reintenta en el siguiente tick.
                db.ChangeTracker.Clear();
                _logger.LogError(ex, "[CfdiMailboxIngestionWorker] Mensaje {Mensaje} falló; se reintenta en el próximo tick.", mensaje.Id);
                salida = new Salida(Resultado.Reintentar);
            }

            switch (salida.Resultado)
            {
                case Resultado.Ingresado: ingresados++; break;
                case Resultado.Duplicado: duplicados++; break;
                case Resultado.Revision:
                    revision++;
                    await EnviarARevisionAsync(mailbox, db, clock, mensaje, salida, cancellationToken);
                    break;
            }
        }

        await RegistrarCorridaAsync(db,
            BuzonEjecucion.Registrar(inicio, clock.UtcNow, mensajes.Count, ingresados, duplicados, revision, null),
            cancellationToken);

        _logger.LogInformation(
            "[CfdiMailboxIngestionWorker] Tick: {Total} mensajes — ingresados={I}, duplicados={D}, a revisión={F}.",
            mensajes.Count, ingresados, duplicados, revision);
    }

    private async Task RegistrarCorridaAsync(CuentasPorPagarDbContext db, BuzonEjecucion corrida, CancellationToken ct)
    {
        try
        {
            db.BuzonEjecuciones.Add(corrida);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La bitácora nunca debe tumbar la ingesta.
            _logger.LogError(ex, "[CfdiMailboxIngestionWorker] No se pudo guardar la bitácora de la corrida.");
        }
    }

    /// <summary>Fila en la bandeja de revisión (idempotente por correo) y el correo sale de Inbox.</summary>
    private async Task EnviarARevisionAsync(
        IMailboxClient mailbox, CuentasPorPagarDbContext db, IClock clock,
        MailboxMensaje mensaje, Salida salida, CancellationToken ct)
    {
        var razon = salida.Razon ?? "DESCONOCIDO";
        try
        {
            if (!await db.BuzonRevisionCfdi.AnyAsync(b => b.MensajeId == mensaje.Id, ct))
            {
                db.BuzonRevisionCfdi.Add(BuzonRevisionCfdi.Registrar(salida.EmpresaId, mensaje, razon, clock.UtcNow));
                await db.SaveChangesAsync(ct);
            }
            // Solo se mueve si quedó registrado: nunca se pierde un correo sin rastro.
            await mailbox.MoverAFallidoAsync(mensaje.Id, razon, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            _logger.LogError(ex,
                "[CfdiMailboxIngestionWorker] No se pudo enviar mensaje {Mensaje} a revisión; se reintenta.", mensaje.Id);
        }
    }

    private async Task<Salida> ProcesarMensajeAsync(
        IMailboxClient mailbox,
        IMediator mediator,
        IXmlCfdiParser parser,
        IDictionary<string, Guid> rfcMap,
        long maxXmlBytes,
        MailboxMensaje mensaje,
        CancellationToken cancellationToken)
    {
        var xmlAttachment = mensaje.Attachments.FirstOrDefault(IsXml);
        if (xmlAttachment is null)
        {
            _logger.LogWarning(
                "[CfdiMailboxIngestionWorker] Mensaje {Mensaje} sin attachment XML; a revisión.", mensaje.Id);
            return new Salida(Resultado.Revision, "SIN_XML");
        }

        if (xmlAttachment.Tamanio is long tam && tam > maxXmlBytes)
            return new Salida(Resultado.Revision, "XML_DEMASIADO_GRANDE");

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
            return new Salida(Resultado.Revision, ex.Codigo);
        }

        if (xmlBytes.LongLength > maxXmlBytes)
            return new Salida(Resultado.Revision, "XML_DEMASIADO_GRANDE");

        DatosCfdiParseados datos;
        try
        {
            using var parseStream = new MemoryStream(xmlBytes, writable: false);
            datos = parser.Parsear(parseStream);
        }
        catch (CfdiParseException ex)
        {
            _logger.LogWarning(ex,
                "[CfdiMailboxIngestionWorker] Mensaje {Mensaje} XML malformado ({Codigo}).",
                mensaje.Id, ex.Codigo);
            return new Salida(Resultado.Revision, ex.Codigo);
        }

        if (!rfcMap.TryGetValue(datos.RfcReceptor.ToUpperInvariant(), out var empresaId))
        {
            _logger.LogWarning(
                "[CfdiMailboxIngestionWorker] Mensaje {Mensaje} RFC receptor no está mapeado en Workers:RfcsReceptores; a revisión.",
                mensaje.Id);
            return new Salida(Resultado.Revision, "RFC_RECEPTOR_NO_MAPEADO");
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
                return new Salida(Resultado.Ingresado);
            }
            catch (BusinessRuleException ex) when (ex.Code == "CFDI_DUPLICADO")
            {
                await mailbox.MoverAProcesadoAsync(mensaje.Id, cancellationToken);
                _logger.LogInformation(
                    "[CfdiMailboxIngestionWorker] UUID duplicado en mensaje {Mensaje}; movido a Processed.",
                    mensaje.Id);
                return new Salida(Resultado.Duplicado);
            }
            catch (BusinessRuleException ex)
            {
                return new Salida(Resultado.Revision, ex.Code, empresaId);
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
