#if DEBUG
using Microsoft.AspNetCore.Mvc;

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
