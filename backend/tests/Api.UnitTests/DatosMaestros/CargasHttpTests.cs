using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Api.Web;
using Millet.DatosMaestros.Application.Cargas;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Idempotency;

namespace Millet.Api.UnitTests.DatosMaestros;

public sealed class CargasHttpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sobre_multipart_excesivo_responde_413_problem_details(bool conLongitud)
    {
        var context = new DefaultHttpContext();
        var limite = ArchivoMaestros.LimiteBytes + 64 * 1024;
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new RequestSizeLimitAttribute(limite)), "Carga"));
        context.Request.Method = "POST";
        context.Request.ContentType = "multipart/form-data; boundary=carga";
        context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        context.Request.Body = new MemoryStream(new byte[limite + 1]);
        if (conLongitud) context.Request.ContentLength = limite + 1;
        var middleware = new IdempotencyMiddleware(_ => throw new InvalidOperationException("No debe ejecutar el endpoint"),
            Options.Create(new IdempotencyOptions()), NullLogger<IdempotencyMiddleware>.Instance);
        var datos = new Contexto();
        var error = await Assert.ThrowsAsync<BadHttpRequestException>(() => middleware.InvokeAsync(context, null!, datos, datos, datos));
        context.Response.Body = new MemoryStream();
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(context, error, default);
        Assert.Equal(413, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(413, problem.RootElement.GetProperty("status").GetInt32());
    }

    private sealed class Contexto : ICurrentUserContext, ICurrentEmpresaContext, IClock
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "Carga ficticia";
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
