using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Idempotency;
using Millet.SharedKernel.Infrastructure.Idempotency;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.Web;

/// <summary>
/// Middleware HTTP que implementa el flujo de idempotencia del ADR-0020.
///
/// <para>
/// Para cada request: lee el header <c>Idempotency-Key</c>, valida formato
/// UUID v4, hashea el body con SHA-256, e intenta INSERT atómico en
/// <c>core.idempotency_keys</c> con <c>ON CONFLICT DO NOTHING</c>. Si era
/// nuevo, ejecuta el handler con captura de response y persiste el resultado.
/// Si ya existía, devuelve la respuesta cacheada o falla con 409/422 según
/// el estado/hash del body. Ver §"Mecánica" del ADR.
/// </para>
/// <para>
/// Decisión de scope: el header SOLO se procesa para métodos de mutación
/// (POST/PUT/PATCH/DELETE). GET/HEAD lo ignoran (son idempotentes nativos).
/// El atributo <see cref="RequireIdempotencyKeyAttribute"/> obliga el header
/// en endpoints decorados; sin él el header es opcional.
/// </para>
/// <para>
/// Revisión 2026-09 (ADR-0020): la key se aísla por endpoint (método + ruta
/// en la PK); los errores del cliente (4xx) no se conservan — la fila se
/// borra y un reintento se ejecuta de nuevo —; los errores del servidor
/// (5xx) quedan <c>failed</c> y sus reintentos reciben 409
/// <c>IDEMPOTENCY_PREVIOUS_FAILURE</c> (posibles efectos parciales en ops
/// fiscales). El replay repite <c>ETag</c> y <c>Location</c>.
/// </para>
/// </summary>
public sealed class IdempotencyMiddleware
{
    private const string HeaderName = "Idempotency-Key";

    private static readonly Regex UuidV4Regex = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly RequestDelegate _next;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(
        RequestDelegate next,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        CoreDbContext db,
        ICurrentEmpresaContext empresaContext,
        ICurrentUserContext userContext,
        IClock clock)
    {
        if (_options.Disabled)
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        var requiresKey = endpoint?.Metadata.GetMetadata<RequireIdempotencyKeyAttribute>() is not null;
        var headerPresent = context.Request.Headers.TryGetValue(HeaderName, out var keyValues);
        var keyValue = headerPresent ? keyValues.ToString() : null;

        // Header ausente: respeto el atributo o paso de largo.
        if (string.IsNullOrEmpty(keyValue))
        {
            if (requiresKey)
            {
                throw new IdempotencyKeyMissingException();
            }

            await _next(context);
            return;
        }

        if (!UuidV4Regex.IsMatch(keyValue))
        {
            throw new IdempotencyKeyInvalidException();
        }

        // Solo aplica a verbos de mutación. GET/HEAD ignoran el header.
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            await _next(context);
            return;
        }

        // Sin tenant/usuario en contexto (endpoint anónimo, fake-login, etc.)
        // el header se ignora: idempotency requiere PK (empresa, usuario, key).
        // Endpoints decorados con [RequireIdempotencyKey] siempre corren tras
        // auth (.RequireAuthorization()), así que en producción este branch
        // solo se toca por requests anonymous con header espurio.
        if (empresaContext.Current is not Guid empresaId
            || userContext.UserId is not Guid usuarioId)
        {
            await _next(context);
            return;
        }

        var bodyHash = await ReadAndHashRequestBodyAsync(context, _options.MaxRequestBodyBytes);
        var correlationId = ResolveCorrelationId(context);
        var now = clock.UtcNow;

        var newRow = new IdempotencyKey
        {
            Key = keyValue,
            EmpresaId = empresaId,
            UsuarioId = usuarioId,
            HttpMethod = method,
            Path = context.Request.Path.Value ?? string.Empty,
            RequestBodyHash = bodyHash,
            Status = IdempotencyStatuses.Processing,
            CorrelationId = correlationId,
            CreatedAt = now,
        };

        var inserted = await TryInsertProcessingRowAsync(db, newRow, context.RequestAborted);

        if (inserted)
        {
            await WarnIfKeyReusedOnOtherEndpointAsync(db, newRow, context.RequestAborted);
            await ExecuteAndCacheAsync(context, db, newRow, clock, context.RequestAborted);
            return;
        }

        // No insertado: ya existe una fila con la misma (empresa, usuario, key)
        // en ESTE endpoint (método + ruta).
        var existing = await db.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.Key == keyValue
                    && x.HttpMethod == newRow.HttpMethod && x.Path == newRow.Path,
                context.RequestAborted)
            ?? throw new InvalidOperationException(
                "INSERT ON CONFLICT no insertó pero SELECT no halla la fila — race extrema.");

        await HandleExistingAsync(context, existing, bodyHash, context.RequestAborted);
    }

    /// <summary>
    /// La misma key en otro endpoint se ejecuta como operación independiente
    /// (la PK incluye método y ruta), pero suele indicar que el cliente no
    /// renovó la key tras una operación previa: se deja rastro para detectarlo.
    /// </summary>
    private async Task WarnIfKeyReusedOnOtherEndpointAsync(
        CoreDbContext db, IdempotencyKey row, CancellationToken ct)
    {
        var reused = await db.IdempotencyKeys
            .AsNoTracking()
            .AnyAsync(
                x => x.EmpresaId == row.EmpresaId && x.UsuarioId == row.UsuarioId && x.Key == row.Key
                    && (x.HttpMethod != row.HttpMethod || x.Path != row.Path),
                ct);

        if (reused)
        {
            _logger.LogWarning(
                "Idempotency-Key reusada en otro endpoint; se ejecuta como operación nueva. {Method} {Path}",
                row.HttpMethod, row.Path);
        }
    }

    private static async Task<string> ReadAndHashRequestBodyAsync(HttpContext context, int maxBytes)
    {
        // EnableBuffering permite que el handler downstream lea el body
        // después de que nosotros lo consumamos para el hash.
        context.Request.EnableBuffering(maxBytes);

        var buffer = new byte[8192];
        using var hasher = SHA256.Create();
        long total = 0;

        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), context.RequestAborted);
            if (read == 0)
            {
                hasher.TransformFinalBlock([], 0, 0);
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw new BadHttpRequestException(
                    $"Request body excede el límite de {maxBytes} bytes para idempotencia.",
                    StatusCodes.Status413PayloadTooLarge);
            }

            hasher.TransformBlock(buffer, 0, read, null, 0);
        }

        context.Request.Body.Position = 0;
        return Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
    }

    private static Guid ResolveCorrelationId(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrEmpty(traceId)
            && Guid.TryParseExact(traceId.PadLeft(32, '0'), "N", out var fromTrace))
        {
            return fromTrace;
        }

        return Guid.CreateVersion7();
    }

    private static async Task<bool> TryInsertProcessingRowAsync(
        CoreDbContext db, IdempotencyKey row, CancellationToken ct)
    {
        // INSERT ON CONFLICT DO NOTHING ... RETURNING 1: si insertó devuelve
        // 1 fila; si conflict, 0 filas. ExecuteSqlRawAsync devuelve el row
        // count afectado, que se mapea directo a inserted/no-inserted.
        const string sql = """
            INSERT INTO core.idempotency_keys (
                key, empresa_id, usuario_id, http_method, path,
                request_body_hash, status, correlation_id, created_at,
                response_body_truncated
            )
            VALUES (
                {0}, {1}, {2}, {3}, {4},
                {5}, 'processing', {6}, {7},
                false
            )
            ON CONFLICT (empresa_id, usuario_id, key, http_method, path) DO NOTHING
            """;

        var affected = await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                row.Key,
                row.EmpresaId,
                row.UsuarioId,
                row.HttpMethod,
                row.Path,
                row.RequestBodyHash,
                row.CorrelationId,
                row.CreatedAt,
            ],
            ct);

        return affected == 1;
    }

    private async Task ExecuteAndCacheAsync(
        HttpContext context,
        CoreDbContext db,
        IdempotencyKey row,
        IClock clock,
        CancellationToken ct)
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        Exception? caught = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        // Status efectivo: si el handler lanzó, todavía no se escribió la
        // respuesta; se resuelve con el mismo mapeo que usará el
        // GlobalExceptionHandler.
        var statusCode = caught is null
            ? context.Response.StatusCode
            : GlobalExceptionHandler.ResolveStatus(caught, context);
        var contentType = context.Response.ContentType ?? string.Empty;

        buffer.Seek(0, SeekOrigin.Begin);
        var responseBytes = buffer.ToArray();

        if (statusCode is >= 400 and < 500)
        {
            // Error del cliente (validación, regla de negocio, conflicto de
            // versión, no encontrado): la transacción del handler no se aplicó,
            // así que no hay efecto que proteger. Se borra la fila para que un
            // reintento — tras corregir la causa — se ejecute de nuevo en lugar
            // de repetir el error o quedar bloqueado.
            await DeleteRowAsync(db, row, ct);
        }
        else if (caught is not null || statusCode >= 500)
        {
            // Error del servidor: pudo dejar efectos parciales. Se conserva como
            // 'failed' y los reintentos con esta key reciben
            // IDEMPOTENCY_PREVIOUS_FAILURE (el cliente debe revisar y usar key nueva).
            await UpdateRowAsync(
                db, row, IdempotencyStatuses.Failed, statusCode,
                responseBody: null, responseHeaders: null, truncated: true, clock.UtcNow, ct);
        }
        else
        {
            var isJson =
                contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase);

            // Sin body (204, 201 vacío) también es replay-able: se repite el status.
            var (responseBody, truncated) = responseBytes.Length == 0
                ? ((string?)null, false)
                : isJson && responseBytes.Length <= _options.MaxResponseBodyBytes
                    ? (Encoding.UTF8.GetString(responseBytes), false)
                    : ((string?)null, true);

            await UpdateRowAsync(
                db, row, IdempotencyStatuses.Completed, statusCode,
                responseBody, CaptureReplayHeaders(context.Response.Headers), truncated, clock.UtcNow, ct);
        }

        if (responseBytes.Length > 0)
        {
            await originalBody.WriteAsync(responseBytes, ct);
        }

        if (caught is not null)
        {
            // Re-lanzar para que GlobalExceptionHandler escriba ProblemDetails.
            throw caught;
        }
    }

    /// <summary>Headers que un replay debe repetir para que el cliente siga funcionando igual.</summary>
    private static readonly string[] ReplayHeaderNames = [HeaderNames.ETag, HeaderNames.Location];

    private static string? CaptureReplayHeaders(IHeaderDictionary headers)
    {
        var captured = new Dictionary<string, string>();
        foreach (var name in ReplayHeaderNames)
        {
            if (headers.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
            {
                captured[name] = value.ToString();
            }
        }

        return captured.Count == 0 ? null : JsonSerializer.Serialize(captured);
    }

    private static async Task DeleteRowAsync(CoreDbContext db, IdempotencyKey row, CancellationToken ct)
        => await db.IdempotencyKeys
            .Where(x => x.EmpresaId == row.EmpresaId && x.UsuarioId == row.UsuarioId && x.Key == row.Key
                && x.HttpMethod == row.HttpMethod && x.Path == row.Path)
            .ExecuteDeleteAsync(ct);

    private static async Task UpdateRowAsync(
        CoreDbContext db,
        IdempotencyKey row,
        string status, int? responseStatus, string? responseBody, string? responseHeaders, bool truncated,
        DateTimeOffset completedAt,
        CancellationToken ct)
    {
        // ExecuteUpdate evita problemas de mapping de DBNull con
        // ExecuteSqlRaw + Npgsql; EF infiere el cast a jsonb por la
        // configuración de la columna.
        await db.IdempotencyKeys
            .Where(x => x.EmpresaId == row.EmpresaId && x.UsuarioId == row.UsuarioId && x.Key == row.Key
                && x.HttpMethod == row.HttpMethod && x.Path == row.Path)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.ResponseStatusCode, responseStatus)
                    .SetProperty(x => x.ResponseBody, responseBody)
                    .SetProperty(x => x.ResponseHeaders, responseHeaders)
                    .SetProperty(x => x.ResponseBodyTruncated, truncated)
                    .SetProperty(x => x.CompletedAt, (DateTimeOffset?)completedAt),
                ct);
    }

    private async Task HandleExistingAsync(
        HttpContext context, IdempotencyKey existing, string requestBodyHash, CancellationToken ct)
    {
        if (existing.Status == IdempotencyStatuses.Processing)
        {
            context.Response.Headers.RetryAfter = _options.RetryAfterSeconds.ToString();
            throw new IdempotencyInProgressException(_options.RetryAfterSeconds);
        }

        if (!string.Equals(existing.RequestBodyHash, requestBodyHash, StringComparison.Ordinal))
        {
            throw new IdempotencyBodyMismatchException();
        }

        if (existing.Status == IdempotencyStatuses.Failed)
        {
            throw new IdempotencyPreviousFailureException();
        }

        if (existing.ResponseBodyTruncated)
        {
            // Completada, pero sin respuesta conservada: no se puede repetir
            // el resultado sin re-ejecutar (evita duplicar ops fiscales).
            throw new IdempotencyResponseTooLargeException();
        }

        context.Response.StatusCode = existing.ResponseStatusCode ?? StatusCodes.Status200OK;
        context.Response.Headers["Idempotent-Replayed"] = "true";

        if (existing.ResponseHeaders is not null)
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(existing.ResponseHeaders);
            foreach (var (name, value) in headers ?? [])
            {
                context.Response.Headers[name] = value;
            }
        }

        if (existing.ResponseBody is not null)
        {
            context.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(existing.ResponseBody);
            await context.Response.Body.WriteAsync(bytes, ct);
        }
    }
}
