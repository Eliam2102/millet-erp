using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Compras.IntegrationTests;

/// <summary>
/// Helpers para crear <see cref="HttpClient"/>s de tests integration que
/// auto-inyectan <c>Idempotency-Key</c> (UUID v4 fresco) en cada request
/// de mutación. Sin esto, los tests que llaman endpoints decorados con
/// <c>[RequireIdempotencyKey]</c> fallarían con
/// <c>400 MISSING_IDEMPOTENCY_KEY</c> (F8-PR1, ADR-0020).
/// </summary>
public static class TestClientExtensions
{
    public static HttpClient CreateClientWithIdempotency<T>(this WebApplicationFactory<T> factory)
        where T : class
        => factory.CreateDefaultClient(new TestIdempotencyHandler());

    private sealed class TestIdempotencyHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsMutation(request.Method) && !request.Headers.Contains("Idempotency-Key"))
            {
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            }
            return base.SendAsync(request, cancellationToken);
        }

        private static bool IsMutation(HttpMethod method) =>
            method == HttpMethod.Post
            || method == HttpMethod.Put
            || method == HttpMethod.Patch
            || method == HttpMethod.Delete;
    }
}
