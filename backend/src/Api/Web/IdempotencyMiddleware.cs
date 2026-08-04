using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
/// Captura de exceptions: si el handler downstream lanza, el
/// <c>GlobalExceptionHandler</c> escribe el Problem Details cuando el body
/// del response ya fue restaurado al stream original — la fila se marca
/// <c>failed</c>+<c>truncated=true</c> y los reintentos reciben 409
/// <c>IDEMPOTENCY_PREVIOUS_FAILURE_NOT_CACHED</c> en vez de re-ejecutar
/// (evita side effects parciales en ops fiscales).
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
            await ExecuteAndCacheAsync(context, db, newRow, clock, context.RequestAborted);
            return;
        }

        // No insertado: ya existe una fila con el mismo (empresa, usuario, key).
        var existing = await db.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.Key == keyValue,
                context.RequestAborted)
            ?? throw new InvalidOperationException(
                "INSERT ON CONFLICT no insertó pero SELECT no halla la fila — race extrema.");

        await HandleExistingAsync(context, existing, bodyHash, context.RequestAborted);
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
            ON CONFLICT (empresa_id, usuario_id, key) DO NOTHING
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

        var statusCode = context.Response.StatusCode;
        var contentType = context.Response.ContentType ?? string.Empty;

        buffer.Seek(0, SeekOrigin.Begin);
        var responseBytes = buffer.ToArray();

        var canCache =
            caught is null
            && responseBytes.Length > 0
            && responseBytes.Length <= _options.MaxResponseBodyBytes
            && (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase));

        var (responseBody, truncated) = canCache
            ? (Encoding.UTF8.GetString(responseBytes), false)
            : ((string?)null, true);

        var newStatus = caught is null && statusCode < 500
            ? IdempotencyStatuses.Completed
            : IdempotencyStatuses.Failed;

        await UpdateRowAsync(
            db, row.EmpresaId, row.UsuarioId, row.Key,
            newStatus, statusCode, responseBody, truncated, clock.UtcNow, ct);

        if (responseBytes.Length > 0)
        {
            await originalBody.WriteAsync(responseBytes, ct);
        }

        if (caught is not null)
        {
            // Re-lanzar para que GlobalExceptionHandler escriba ProblemDetails
            // en el response. Nuestra caché quedó marked truncated; los
            // retries reciben IDEMPOTENCY_PREVIOUS_FAILURE_NOT_CACHED.
            throw caught;
        }
    }

    private static async Task UpdateRowAsync(
        CoreDbContext db,
        Guid empresaId, Guid usuarioId, string key,
        string status, int? responseStatus, string? responseBody, bool truncated,
        DateTimeOffset completedAt,
        CancellationToken ct)
    {
        // ExecuteUpdate evita problemas de mapping de DBNull con
        // ExecuteSqlRaw + Npgsql; EF infiere el cast a jsonb por la
        // configuración de la columna.
        await db.IdempotencyKeys
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.Key == key)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.ResponseStatusCode, responseStatus)
                    .SetProperty(x => x.ResponseBody, responseBody)
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

        if (existing.ResponseBodyTruncated || existing.ResponseBody is null)
        {
            // No tenemos respuesta cacheable (response demasiado grande o
            // handler falló sin body): retry no puede replay sin re-ejecutar.
            // Se rechaza para evitar side effects parciales en ops fiscales.
            throw new IdempotencyInProgressException(_options.RetryAfterSeconds);
        }

        context.Response.StatusCode = existing.ResponseStatusCode ?? StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        context.Response.Headers["Idempotent-Replayed"] = "true";

        var bytes = Encoding.UTF8.GetBytes(existing.ResponseBody);
        await context.Response.Body.WriteAsync(bytes, ct);
    }
}
