#if DEBUG
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Web;

/// <summary>
/// Endpoint de prueba para validar el <see cref="IdempotencyMiddleware"/>
/// en tests integration (F8-PR1). Compilado solo en Debug — el binario
/// de Release no lo contiene (mismo patrón que <c>DevAuthEndpoints</c>,
/// ADR-0015).
///
/// <para>
/// El endpoint incrementa un contador estático y echoa el body recibido.
/// Si el middleware funciona correctamente, los retries con la misma
/// <c>Idempotency-Key</c> devuelven la respuesta cacheada (mismo
/// <c>count</c>) sin re-ejecutar el handler.
/// </para>
/// </summary>
public static class DevIdempotencyEndpoints
{
    private static int _counter;

    // Cuántas veces se ha ejecutado cada payload en los endpoints de fallo;
    // permite que la primera ejecución falle y la segunda tenga éxito.
    private static readonly ConcurrentDictionary<string, int> _intentos = new();

    public static IEndpointRouteBuilder MapDevIdempotencyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/idempotent-echo", (
            [FromBody] IdempotentEchoRequest request) =>
        {
            var count = Interlocked.Increment(ref _counter);
            return Results.Ok(new IdempotentEchoResponse(count, request.Payload));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization();

        // Misma lógica con id en la ruta: la misma key en otro {id} es otra
        // operación (revisión 2026-09 del ADR-0020). Devuelve ETag para
        // validar que el replay lo repite.
        app.MapPost("/api/dev/idempotent-echo/{id}", (
            string id,
            [FromBody] IdempotentEchoRequest request,
            HttpContext http) =>
        {
            var count = Interlocked.Increment(ref _counter);
            http.Response.Headers.ETag = $"\"{id}-{count}\"";
            return Results.Ok(new IdempotentEchoResponse(count, request.Payload));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization();

        // Primera ejecución de cada payload: error de negocio (409); la
        // segunda: éxito. Valida que un 4xx no bloquee el reintento.
        app.MapPost("/api/dev/idempotent-fail-4xx", (
            [FromBody] IdempotentEchoRequest request) =>
        {
            if (_intentos.AddOrUpdate(request.Payload, 1, (_, n) => n + 1) == 1)
            {
                throw new ConflictException("DEV_CONFLICTO", "Conflicto simulado en el primer intento.");
            }

            var count = Interlocked.Increment(ref _counter);
            return Results.Ok(new IdempotentEchoResponse(count, request.Payload));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization();

        // Siempre falla con error de servidor (5xx).
        app.MapPost("/api/dev/idempotent-fail-5xx", (
            [FromBody] IdempotentEchoRequest request) =>
        {
            _intentos.AddOrUpdate(request.Payload, 1, (_, n) => n + 1);
            throw new InvalidOperationException("Falla de servidor simulada.");
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization();

        // Endpoint NO decorado para validar que el middleware ignora
        // requests sin header cuando el endpoint no exige idempotencia.
        app.MapPost("/api/dev/non-idempotent-echo", (
            [FromBody] IdempotentEchoRequest request) =>
        {
            var count = Interlocked.Increment(ref _counter);
            return Results.Ok(new IdempotentEchoResponse(count, request.Payload));
        })
        .RequireAuthorization();

        return app;
    }
}

public sealed record IdempotentEchoRequest(string Payload);

public sealed record IdempotentEchoResponse(int Count, string Payload);
#endif
