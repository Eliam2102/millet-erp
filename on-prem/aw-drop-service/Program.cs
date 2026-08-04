using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Millet.AwDropService;

// ============================================================================
// Millet A+W Drop Service
// .NET 8 Minimal API. Recibe archivos EDI vía HTTP en localhost:5000 desde
// el módulo Millet.Integraciones.Aw (a través de Azure Hybrid Connection),
// los escribe a la carpeta Work\ de A+W, ESPERA a que A+W los procese
// (per-EDI flow, PR #199) y reporta el outcome final en la misma respuesta.
//
// Endpoints:
//   GET  /             → smoke
//   GET  /healthz      → liveness + writable check de cada lane
//   POST /drop-edi     → drop síncrono por-EDI con outcome A+W
//
// El servicio NUNCA debe escuchar en interfaces externas: la única forma de
// alcanzarlo desde el ERP es a través del HCM que forwardea localhost:5000
// desde Azure Relay.
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "MilletAwDropService";
});

if (OperatingSystem.IsWindows())
{
    AddWindowsEventLog(builder);
}

builder.Services
    .AddOptions<DropServiceOptions>()
    .Bind(builder.Configuration.GetSection(DropServiceOptions.SectionName))
    .ValidateDataAnnotations();

// Carpetas de PDF que A+W exporta (ofertas/pedidos). Si la sección no existe,
// la lista queda vacía y GET /documents devuelve [] (no es error).
builder.Services
    .AddOptions<AwDocumentsOptions>()
    .Bind(builder.Configuration.GetSection(AwDocumentsOptions.SectionName));

// Serializador del flujo per-EDI: 1 EDI en vuelo a la vez. Con un único
// customizing (single-pass) y marcador por-EDI ya no es estrictamente
// necesario para desambiguar, pero se conserva para acotar cuántos EDI hay
// en Work\ a la vez.
builder.Services.AddSingleton(new SemaphoreSlim(initialCount: 1, maxCount: 1));
builder.Services.AddSingleton<MarkerWatcher>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<DropServiceOptions>>().Value;
    return new MarkerWatcher(opts.PollIntervalMs);
});

// Tamaño máximo del body (Kestrel). Si el módulo del ERP envía un EDI mayor,
// Kestrel rechaza con 413 antes de llegar al endpoint. Default 10 MB.
var maxBodyMb = builder.Configuration.GetValue($"{DropServiceOptions.SectionName}:MaxBodySizeMB", 10);
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.Limits.MaxRequestBodySize = maxBodyMb * 1024L * 1024L;
});

// URL de escucha. Default localhost:5000. JAMÁS bindear a una interfaz
// externa: el acceso desde el ERP entra vía HCM forwardeando localhost.
// En tests WebApplicationFactory<Program> ignora esto y usa TestServer
// in-memory.
var listenUrl = builder.Configuration[$"{DropServiceOptions.SectionName}:ListenUrl"] ?? "http://localhost:5000";
builder.WebHost.UseUrls(listenUrl);

var app = builder.Build();

var startedAtUtc = DateTimeOffset.UtcNow;
var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

// ============================================================================
// GET / — smoke test desde browser. Texto plano, sin auth.
// ============================================================================
app.MapGet("/", () => Results.Text($"Millet A+W Drop Service - v{version}", "text/plain"));

// ============================================================================
// GET /healthz — liveness para HCM y operaciones. Sin auth (es local).
// Checa WorkDir (donde A+W lee los EDI) + ResultsDir (donde el customizing
// escribe el marcador cot_<REF>.<docid>) por writability. Status writable =
// OK solo si ambos lo están.
// ============================================================================
app.MapGet("/healthz", (IOptions<DropServiceOptions> dropOpts) =>
{
    var s = dropOpts.Value;
    var workWritable = ProbeWritable(s.AwImportFolder);
    var resultsWritable = ProbeWritable(s.ResolvedResultsFolder);

    return Results.Ok(new
    {
        status = "alive",
        version,
        uptime_seconds = (long)(DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds,
        // Campo legacy mantenido para HCM/scripts que aún lo lean.
        import_folder_writable = workWritable && resultsWritable,
        work_dir = s.AwImportFolder,
        results_dir = s.ResolvedResultsFolder,
        work_writable = workWritable,
        results_writable = resultsWritable,
    });
});

// ============================================================================
// GET /completions — lista los MARCADORES de Results\ desde un cursor `since`
// (ISO 8601). Cursor-based pagination ordenada por ParsedAt (mtime) ASC. Lo
// consume el AwLateReconciliationWorker del ERP para recuperar EDIs que A+W
// procesó después del timeout sync del drop service. Cada item lleva
// outcome="success" + aw_doc_id (el marcador solo existe en éxito).
//
// Auth: misma X-API-Key que /drop-edi.
// Query:
//   since? ISO 8601 (estrictamente posterior; default: sin filtro)
//   limit? int (default 500, max 2000)
// Response 200:
//   { completions: [...], next_since: "2026-05-29T10:33:12.118Z" | null }
// ============================================================================
const int CompletionsDefaultLimit = 500;
const int CompletionsMaxLimit = 2000;

app.MapGet("/completions", (
    HttpRequest req,
    IOptions<DropServiceOptions> opts,
    ILoggerFactory loggerFactory) =>
{
    var log = loggerFactory.CreateLogger("CompletionsEndpoint");
    var settings = opts.Value;
    var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Auth idéntica a /drop-edi (constant-time compare).
    if (string.IsNullOrEmpty(settings.ApiKey))
    {
        log.LogError("Completions rechazado: DropService:ApiKey no configurada.");
        return Results.Unauthorized();
    }
    var provided = req.Headers["X-API-Key"].ToString();
    if (string.IsNullOrEmpty(provided) || !ConstantTimeEquals(provided, settings.ApiKey))
    {
        log.LogWarning("Completions unauthorized remote_ip={RemoteIp} reason={Reason}",
            remoteIp, string.IsNullOrEmpty(provided) ? "missing_key" : "wrong_key");
        return Results.Unauthorized();
    }

    // since query
    DateTimeOffset? sinceParsed = null;
    var sinceRaw = req.Query["since"].ToString();
    if (!string.IsNullOrWhiteSpace(sinceRaw))
    {
        if (!DateTimeOffset.TryParse(sinceRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedSince))
        {
            return Results.BadRequest(new { error = "since inválido (ISO 8601 esperado)" });
        }
        sinceParsed = parsedSince;
    }

    // limit query con clamp [1, 2000]
    var effectiveLimit = CompletionsDefaultLimit;
    var limitRaw = req.Query["limit"].ToString();
    if (!string.IsNullOrWhiteSpace(limitRaw))
    {
        if (!int.TryParse(limitRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit))
        {
            return Results.BadRequest(new { error = "limit inválido (entero esperado)" });
        }
        effectiveLimit = Math.Clamp(parsedLimit, 1, CompletionsMaxLimit);
    }

    var items = ResultsReader.ListSince(
        opts.Value.ResolvedResultsFolder, sinceParsed, effectiveLimit, log);

    var nextSince = items.Count > 0 ? items[^1].ParsedAt : (DateTimeOffset?)null;

    log.LogInformation(
        "Completions listed since={Since} limit={Limit} returned={Returned} remote_ip={RemoteIp}",
        sinceParsed, effectiveLimit, items.Count, remoteIp);

    return Results.Ok(new
    {
        completions = items.Select(c => new
        {
            filename = c.Filename,
            outcome = c.Outcome,
            aw_doc_id = c.AwDocId,
            aw_error_codes = c.AwErrorCodes,
            aw_error_message = c.AwErrorMessage,
            aw_diagnostic_log = c.AwDiagnosticLog,
            parsed_at = c.ParsedAt,
            lane = c.Lane,
        }),
        next_since = nextSince,
    });
});

// ============================================================================
// GET /documents — lista los PDF que A+W exporta en las carpetas configuradas
// (oferta_<nro>.pdf / pedido_<nro>.pdf). Cursor-based pagination ordenada por
// ModifiedAt ASC. Lo consume el AwDocumentSyncWorker del ERP para subir cada
// PDF a Blob Storage y publicar la URL al Glass Agent.
//
// Auth: misma X-API-Key que /drop-edi y /completions.
// Query:
//   since? ISO 8601 (estrictamente posterior; default: sin filtro)
//   limit? int (default 500, max 2000)
// Response 200:
//   { documents: [...], next_since: "2026-06-30T10:33:12.118Z" | null }
// ============================================================================
const int DocumentsDefaultLimit = 500;
const int DocumentsMaxLimit = 2000;

app.MapGet("/documents", (
    HttpRequest req,
    IOptions<DropServiceOptions> opts,
    IOptions<AwDocumentsOptions> docsOpts,
    ILoggerFactory loggerFactory) =>
{
    var log = loggerFactory.CreateLogger("DocumentsEndpoint");
    var settings = opts.Value;
    var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Auth idéntica a /drop-edi (constant-time compare).
    if (string.IsNullOrEmpty(settings.ApiKey))
    {
        log.LogError("Documents rechazado: DropService:ApiKey no configurada.");
        return Results.Unauthorized();
    }
    var provided = req.Headers["X-API-Key"].ToString();
    if (string.IsNullOrEmpty(provided) || !ConstantTimeEquals(provided, settings.ApiKey))
    {
        log.LogWarning("Documents unauthorized remote_ip={RemoteIp} reason={Reason}",
            remoteIp, string.IsNullOrEmpty(provided) ? "missing_key" : "wrong_key");
        return Results.Unauthorized();
    }

    // since query
    DateTimeOffset? sinceParsed = null;
    var sinceRaw = req.Query["since"].ToString();
    if (!string.IsNullOrWhiteSpace(sinceRaw))
    {
        if (!DateTimeOffset.TryParse(sinceRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedSince))
        {
            return Results.BadRequest(new { error = "since inválido (ISO 8601 esperado)" });
        }
        sinceParsed = parsedSince;
    }

    // limit query con clamp [1, 2000]
    var effectiveLimit = DocumentsDefaultLimit;
    var limitRaw = req.Query["limit"].ToString();
    if (!string.IsNullOrWhiteSpace(limitRaw))
    {
        if (!int.TryParse(limitRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit))
        {
            return Results.BadRequest(new { error = "limit inválido (entero esperado)" });
        }
        effectiveLimit = Math.Clamp(parsedLimit, 1, DocumentsMaxLimit);
    }

    var folders = docsOpts.Value.Folders;
    var items = DocumentsReader.ListSince(folders, sinceParsed, effectiveLimit, log);

    var nextSince = items.Count > 0 ? items[^1].ModifiedAt : (DateTimeOffset?)null;

    log.LogInformation(
        "Documents listed since={Since} limit={Limit} returned={Returned} remote_ip={RemoteIp}",
        sinceParsed, effectiveLimit, items.Count, remoteIp);

    return Results.Ok(new
    {
        documents = items.Select(d => new
        {
            filename = d.Filename,
            doc_type = d.DocType,
            aw_doc_id = d.AwDocId,
            size_bytes = d.SizeBytes,
            modified_at = d.ModifiedAt,
        }),
        next_since = nextSince,
    });
});

// ============================================================================
// GET /documents/{filename} — descarga los bytes de un PDF exportado por A+W.
// El filename debe matchear la convención (oferta_<nro>.pdf / pedido_<nro>.pdf)
// y resolverse dentro de alguna carpeta configurada (path-traversal check).
//
// Auth: misma X-API-Key. Response: 200 application/pdf | 400 (nombre inválido)
// | 404 (no encontrado) | 401.
// ============================================================================
app.MapGet("/documents/{filename}", (
    string filename,
    HttpRequest req,
    IOptions<DropServiceOptions> opts,
    IOptions<AwDocumentsOptions> docsOpts,
    ILoggerFactory loggerFactory) =>
{
    var log = loggerFactory.CreateLogger("DocumentsDownloadEndpoint");
    var settings = opts.Value;
    var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (string.IsNullOrEmpty(settings.ApiKey))
    {
        log.LogError("Documents download rechazado: DropService:ApiKey no configurada.");
        return Results.Unauthorized();
    }
    var provided = req.Headers["X-API-Key"].ToString();
    if (string.IsNullOrEmpty(provided) || !ConstantTimeEquals(provided, settings.ApiKey))
    {
        log.LogWarning("Documents download unauthorized remote_ip={RemoteIp} reason={Reason}",
            remoteIp, string.IsNullOrEmpty(provided) ? "missing_key" : "wrong_key");
        return Results.Unauthorized();
    }

    if (!DocumentsReader.TryResolve(docsOpts.Value.Folders, filename, out var fullPath))
    {
        log.LogWarning("Documents download not_found_or_invalid filename={Filename} remote_ip={RemoteIp}",
            filename, remoteIp);
        // No distinguimos nombre inválido vs inexistente para no filtrar la
        // estructura de carpetas: 404 en ambos casos.
        return Results.NotFound(new { error = "documento no encontrado" });
    }

    log.LogInformation("Documents download served filename={Filename} remote_ip={RemoteIp}",
        filename, remoteIp);

    var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    return Results.File(stream, "application/pdf", fileDownloadName: filename);
});

// ============================================================================
// POST /documents/{filename}/archive — mueve un PDF YA TRATADO por el ERP a
// la subcarpeta archive\ (renombrado con _<utcTimestamp>). El ERP lo llama
// SOLO después de subir el PDF a Blob Storage y persistir, para que la
// carpeta caliente se autolimpie y el scan no escale con el histórico.
//
// Idempotente: si el archivo ya no está (ack repetido) → 200 con
// archived=false. Auth: misma X-API-Key.
// ============================================================================
app.MapPost("/documents/{filename}/archive", (
    string filename,
    HttpRequest req,
    IOptions<DropServiceOptions> opts,
    IOptions<AwDocumentsOptions> docsOpts,
    ILoggerFactory loggerFactory) =>
{
    var log = loggerFactory.CreateLogger("DocumentsArchiveEndpoint");
    var settings = opts.Value;
    var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (string.IsNullOrEmpty(settings.ApiKey))
    {
        log.LogError("Documents archive rechazado: DropService:ApiKey no configurada.");
        return Results.Unauthorized();
    }
    var provided = req.Headers["X-API-Key"].ToString();
    if (string.IsNullOrEmpty(provided) || !ConstantTimeEquals(provided, settings.ApiKey))
    {
        log.LogWarning("Documents archive unauthorized remote_ip={RemoteIp} reason={Reason}",
            remoteIp, string.IsNullOrEmpty(provided) ? "missing_key" : "wrong_key");
        return Results.Unauthorized();
    }

    var outcome = DocumentsReader.Archivar(
        docsOpts.Value.Folders, filename, DateTimeOffset.UtcNow, log, out var archivedPath);

    return outcome switch
    {
        ArchiveOutcome.Archived => Results.Ok(new { filename, archived = true, archived_path = archivedPath }),
        // NotFound es éxito idempotente: el ERP no debe reintentar.
        ArchiveOutcome.NotFound => Results.Ok(new { filename, archived = false }),
        _ => Results.Problem("No se pudo archivar el documento", statusCode: 500),
    };
});

// ============================================================================
// POST /results/{name}/archive — mueve un MARCADOR ya procesado por el ERP a
// Results\archive\ (renombrado con _<utcTimestamp>). El ERP lo llama SOLO
// después de grabar el aw_doc_id (el move = "procesado"): así Results\ queda
// con los pendientes y el scan de /completions no escala con el histórico.
//
// Idempotente: si el marcador ya no está (ack repetido) → 200 archived=false.
// Auth: misma X-API-Key.
// ============================================================================
app.MapPost("/results/{name}/archive", (
    string name,
    HttpRequest req,
    IOptions<DropServiceOptions> opts,
    ILoggerFactory loggerFactory) =>
{
    var log = loggerFactory.CreateLogger("ResultsArchiveEndpoint");
    var settings = opts.Value;
    var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (string.IsNullOrEmpty(settings.ApiKey))
    {
        log.LogError("Results archive rechazado: DropService:ApiKey no configurada.");
        return Results.Unauthorized();
    }
    var provided = req.Headers["X-API-Key"].ToString();
    if (string.IsNullOrEmpty(provided) || !ConstantTimeEquals(provided, settings.ApiKey))
    {
        log.LogWarning("Results archive unauthorized remote_ip={RemoteIp} reason={Reason}",
            remoteIp, string.IsNullOrEmpty(provided) ? "missing_key" : "wrong_key");
        return Results.Unauthorized();
    }

    var outcome = ResultsReader.Archivar(
        settings.ResolvedResultsFolder, name, DateTimeOffset.UtcNow, log, out var archivedPath);

    return outcome switch
    {
        ArchiveOutcome.Archived => Results.Ok(new { name, archived = true, archived_path = archivedPath }),
        // NotFound es éxito idempotente: el ERP no debe reintentar.
        ArchiveOutcome.NotFound => Results.Ok(new { name, archived = false }),
        _ => Results.Problem("No se pudo archivar el marcador", statusCode: 500),
    };
});

// ============================================================================
// POST /drop-edi — recibe archivo EDI, lo entrega a A+W per-EDI y reporta
// el outcome final.
//
// Pasos:
//   1. Auth: X-API-Key matchea config (constant-time compare).
//   2. Filename: regex ^[a-zA-Z0-9_.-]+\.edi$.
//   3. Body: ≥ MinBodyBytes + contiene #END#.
//   4. Serializar (semáforo — 1 EDI en vuelo).
//   5. Path resolve + traversal check contra WorkDir.
//   6. Escritura atómica: bytes → {filename}.tmp → File.Move({filename}).
//   7. Wait por el MARCADOR cot_<REF>.<docid> en Results\ (timeout config).
//   8. Respond: success + aw_doc_id (del nombre) si apareció; stuck si timeout.
//
// El contenido del EDI NUNCA se loggea (puede contener datos de clientes).
// Bajo el modelo de marcador (Idea 4) no hay contenido de log que parsear ni
// que devolver: el nombre del marcador ES el pedido. A+W solo escribe el
// marcador en éxito; si rechaza el EDI no escribe nada → timeout=stuck.
// ============================================================================
var filenameRegex = new Regex(@"^[a-zA-Z0-9_.-]+\.edi$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));
const int MinBodyBytes = 100;
const string EdiEndMarker = "#END#";

app.MapPost("/drop-edi", async (
    HttpRequest req,
    IOptions<DropServiceOptions> opts,
    SemaphoreSlim gate,
    MarkerWatcher watcher,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var log = loggerFactory.CreateLogger("DropEndpoint");
    var sw = Stopwatch.StartNew();
    var remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var settings = opts.Value;

    // 1. API key.
    if (string.IsNullOrEmpty(settings.ApiKey))
    {
        log.LogError("Drop request rechazado: DropService:ApiKey no configurada.");
        return Results.Unauthorized();
    }

    var provided = req.Headers["X-API-Key"].ToString();
    if (string.IsNullOrEmpty(provided) || !ConstantTimeEquals(provided, settings.ApiKey))
    {
        log.LogWarning("Drop unauthorized remote_ip={RemoteIp} reason={Reason}",
            remoteIp, string.IsNullOrEmpty(provided) ? "missing_key" : "wrong_key");
        return Results.Unauthorized();
    }

    // 2. Filename.
    var filename = req.Headers["X-Filename"].ToString();
    if (string.IsNullOrWhiteSpace(filename) || !filenameRegex.IsMatch(filename))
    {
        log.LogWarning("Drop bad_request reason=invalid_filename filename={Filename} remote_ip={RemoteIp}",
            filename, remoteIp);
        return Results.BadRequest(new { error = "filename inválido" });
    }

    // 3. Leer body.
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms, cancellationToken);
    var content = ms.ToArray();

    if (content.Length == 0)
    {
        log.LogWarning("Drop bad_request reason=empty_body filename={Filename}", filename);
        return Results.BadRequest(new { error = "contenido vacío" });
    }
    if (content.Length < MinBodyBytes)
    {
        log.LogWarning("Drop bad_request reason=body_too_small filename={Filename} bytes={Bytes}",
            filename, content.Length);
        return Results.BadRequest(new { error = $"contenido muy pequeño (<{MinBodyBytes} bytes)" });
    }

    string ediText;
    try
    {
        ediText = Encoding.UTF8.GetString(content);
    }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Drop unprocessable reason=not_utf8 filename={Filename} bytes={Bytes}",
            filename, content.Length);
        return Results.UnprocessableEntity(new { error = "contenido no es UTF-8 válido" });
    }
    if (!ediText.Contains(EdiEndMarker, StringComparison.Ordinal))
    {
        log.LogWarning("Drop unprocessable reason=missing_end_marker filename={Filename} bytes={Bytes}",
            filename, content.Length);
        return Results.UnprocessableEntity(new { error = $"EDI sin marca {EdiEndMarker}" });
    }

    // 4. Serializar: adquirir el semáforo (1 EDI en vuelo). Acota cuántos EDI
    // hay en Work\ a la vez. Se libera al salir del handler (incluidos los
    // early-return de abajo).
    await gate.WaitAsync(cancellationToken);
    using var _ = new SemaphoreReleaser(gate);

    // 5. Path resolve + traversal check.
    string workFolder;
    string targetFull;
    try
    {
        workFolder = Path.GetFullPath(settings.AwImportFolder);
        targetFull = Path.GetFullPath(Path.Combine(workFolder, filename));
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Drop bad_request reason=path_resolve_failed filename={Filename}",
            filename);
        return Results.BadRequest(new { error = "filename inválido" });
    }

    var folderWithSep = workFolder.EndsWith(Path.DirectorySeparatorChar)
        ? workFolder
        : workFolder + Path.DirectorySeparatorChar;
    if (!targetFull.StartsWith(folderWithSep, StringComparison.OrdinalIgnoreCase))
    {
        log.LogWarning("Drop bad_request reason=path_traversal filename={Filename} resolved={Target}",
            filename, targetFull);
        return Results.BadRequest(new { error = "filename resuelve fuera de WorkDir" });
    }

    // 6. Escritura atómica.
    Directory.CreateDirectory(workFolder);
    var tmp = targetFull + ".tmp";
    DateTime droppedAtUtc;
    try
    {
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fs.WriteAsync(content, cancellationToken);
            await fs.FlushAsync(cancellationToken);
        }
        if (File.Exists(targetFull))
        {
            File.Delete(targetFull);
        }
        File.Move(tmp, targetFull);
        droppedAtUtc = DateTime.UtcNow;

        log.LogInformation(
            "Drop file written filename={Filename} bytes={Bytes} remote_ip={RemoteIp}",
            filename, content.Length, remoteIp);
    }
    catch (Exception ex)
    {
        if (File.Exists(tmp))
        {
            try { File.Delete(tmp); }
            catch { /* swallow — best effort cleanup */ }
        }
        sw.Stop();
        log.LogError(ex,
            "Drop io_error filename={Filename} bytes={Bytes} duration_ms={Duration}",
            filename, content.Length, sw.ElapsedMilliseconds);
        return Results.Problem("Error escribiendo archivo", statusCode: 500);
    }

    // 7. Wait por el marcador cot_<REF>.<docid> en Results\ (lo escribe el
    // customizing tras crear el pedido). Si timeout sin marcador → stuck
    // (reintentable): cubre "aún no procesado" y "A+W rechazó" (Idea 4).
    var waitTimeout = TimeSpan.FromSeconds(settings.WaitTimeoutSeconds);
    var waitResult = await watcher.WaitForMarkerAsync(
        settings.ResolvedResultsFolder, filename, droppedAtUtc, waitTimeout, cancellationToken);

    sw.Stop();

    var outcomeStr = waitResult.Found ? "success" : "stuck";

    log.LogInformation(
        "Drop done filename={Filename} bytes={Bytes} outcome={Outcome} aw_doc_id={AwDocId} " +
        "waited_ms={WaitedMs} duration_ms={Duration} remote_ip={RemoteIp}",
        filename, content.Length, outcomeStr, waitResult.AwDocId,
        waitResult.WaitedMs, sw.ElapsedMilliseconds, remoteIp);

    return Results.Ok(new
    {
        filename,
        path = targetFull,
        bytes_written = content.Length,
        written_at = droppedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        outcome = outcomeStr,
        aw_doc_id = waitResult.AwDocId,
        // El marcador no lleva diagnóstico (Idea 4). Campos conservados en el
        // contrato para no romper la deserialización del ERP.
        aw_error_codes = Array.Empty<string>(),
        aw_error_message = (string?)null,
        aw_diagnostic_log = (string?)null,
        // Constante en el contrato de respuesta: el ERP (Millet.Integraciones.Aw)
        // aún deserializa este campo. Ya no hay routing por lane on-prem.
        lane = DropServiceOptions.WireLane,
        waited_ms = waitResult.WaitedMs,
    });
});

app.Run();

// ============================================================================
// Helpers
// ============================================================================

[SupportedOSPlatform("windows")]
static void AddWindowsEventLog(WebApplicationBuilder builder)
{
    // EventLog sink Windows-only. La source 'MilletAwDropService' se crea
    // con New-EventLog en install/install-service.ps1; si no existe, el
    // sink falla silenciosamente y los logs solo van a consola.
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = builder.Configuration["Logging:EventLog:SourceName"] ?? "MilletAwDropService";
    });
}

static bool ConstantTimeEquals(string a, string b)
{
    // Convertir a bytes UTF-8 antes de comparar. FixedTimeEquals exige
    // longitudes iguales — si difieren retornamos false. La longitud sí es
    // información leakable; el objetivo es no leakear contenido cuando los
    // largos coinciden (caso normal de API key bien formateada).
    var aBytes = Encoding.UTF8.GetBytes(a);
    var bBytes = Encoding.UTF8.GetBytes(b);
    if (aBytes.Length != bBytes.Length)
    {
        return false;
    }
    return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
}

static bool ProbeWritable(string folder)
{
    try
    {
        Directory.CreateDirectory(folder);
        var probe = Path.Combine(folder, $".healthz-{Guid.NewGuid():N}.probe");
        File.WriteAllText(probe, string.Empty);
        File.Delete(probe);
        return true;
    }
    catch
    {
        return false;
    }
}

// ============================================================================
// Options
// ============================================================================

public sealed class DropServiceOptions
{
    public const string SectionName = "DropService";

    /// <summary>
    /// Valor constante del campo <c>lane</c> en las respuestas de
    /// <c>/drop-edi</c> y <c>/completions</c>. Ya no hay routing por lane
    /// on-prem (un único customizing single-pass); el campo se conserva en
    /// el contrato porque el ERP (<c>Millet.Integraciones.Aw</c>) aún lo
    /// deserializa.
    /// </summary>
    public const string WireLane = "default";

    public string ListenUrl { get; init; } = "http://localhost:5000";
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Carpeta donde el drop service escribe los EDI y A+W los lee (WorkDir).
    /// A+W mueve a Save\ o Fail\ tras procesar.
    ///
    /// // PLATFORM-TODO(&lt;AwImportFolderPath&gt;): default "C:\\AW\\Import" es
    /// placeholder. La ruta real depende de la instalación de A+W en
    /// SER-DATA — pendiente confirmar con equipo A+W de Millet. Ver
    /// `docs/integration/00-system-overview.md` §4.4.
    /// </summary>
    public string AwImportFolder { get; init; } = @"C:\AW\Import";

    /// <summary>
    /// Carpeta donde el customizing de A+W escribe el marcador
    /// <c>cot_&lt;REF&gt;.&lt;AWDOCID&gt;</c> (vacío) tras crear el pedido, y
    /// donde el drop service crea el subdirectorio <c>archive\</c>. Si se deja
    /// vacío, se resuelve a <c>{AwImportFolder}\Results</c> (ver
    /// <see cref="ResolvedResultsFolder"/>).
    /// </summary>
    public string? ResultsFolder { get; init; }

    /// <summary>
    /// Timeout esperando que A+W procese el EDI (escriba el marcador) tras el
    /// drop. El scheduler de A+W corre cada ~60s; dos ciclos dan margen. Si
    /// timeout, outcome=stuck → el ERP marca FailedDrop reintentable.
    /// </summary>
    [Range(1, 600, ErrorMessage = "WaitTimeoutSeconds debe estar entre 1 y 600.")]
    public int WaitTimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Cadencia de polling sobre <c>Results\</c> buscando el marcador. Bajar
    /// acorta la latencia pero aumenta IO inútil.
    /// </summary>
    [Range(100, 10000, ErrorMessage = "PollIntervalMs debe estar entre 100 y 10000.")]
    public int PollIntervalMs { get; init; } = 1000;

    public int MaxBodySizeMB { get; init; } = 10;
    public int WriteTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Ruta efectiva de resultados: <see cref="ResultsFolder"/> si está
    /// definida, o <c>{AwImportFolder}\Results</c> por convención.
    /// </summary>
    public string ResolvedResultsFolder =>
        string.IsNullOrWhiteSpace(ResultsFolder)
            ? Path.Combine(AwImportFolder, "Results")
            : ResultsFolder;
}

/// <summary>
/// Disposable RAII que libera un <see cref="SemaphoreSlim"/> al salir de un
/// bloque <c>using</c>. Reemplaza al antiguo LaneLease: garantiza el release
/// del semáforo del drop incluso en los early-return del handler.
/// </summary>
internal readonly struct SemaphoreReleaser : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    public SemaphoreReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
    public void Dispose() => _semaphore.Release();
}

// Para que tests/DropEndpointTests.cs pueda usar WebApplicationFactory<Program>.
public partial class Program;
