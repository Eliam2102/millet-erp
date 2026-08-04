using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Api.Web;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Api.IntegrationTests.Web;

/// <summary>
/// Tests sobre <see cref="GlobalExceptionHandler"/>. Aunque viven en el
/// proyecto de IntegrationTests por la dependencia con tipos de
/// AspNetCore (HttpContext, etc.), estas pruebas son unit-style: usan
/// <see cref="DefaultHttpContext"/> sin levantar el host completo.
/// </summary>
public class GlobalExceptionHandlerTests
{
    private static async Task<(int Status, string Code, string ContentType)> Handle(Exception exception)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/test";

        var handled = await handler.TryHandleAsync(ctx, exception, CancellationToken.None);
        handled.Should().BeTrue();

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        var code = doc.RootElement.GetProperty("code").GetString() ?? string.Empty;
        return (ctx.Response.StatusCode, code, ctx.Response.ContentType ?? string.Empty);
    }

    [Fact]
    public async Task Should_Map_BusinessRuleException_To_422()
    {
        var (status, code, contentType) = await Handle(new BusinessRuleException("RFC_DUPLICADO", "Ya existe"));

        status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        code.Should().Be("RFC_DUPLICADO");
        contentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task Should_Map_EntityNotFoundException_To_404()
    {
        var (status, code, _) = await Handle(new EntityNotFoundException("CLIENTE_INEXISTENTE", "no existe"));

        status.Should().Be(StatusCodes.Status404NotFound);
        code.Should().Be("CLIENTE_INEXISTENTE");
    }

    [Fact]
    public async Task Should_Map_ConcurrencyException_To_409()
    {
        var (status, code, _) = await Handle(new ConcurrencyException("Cliente", Guid.NewGuid()));

        status.Should().Be(StatusCodes.Status409Conflict);
        code.Should().Be("CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task Should_Map_ForbiddenException_To_403()
    {
        var (status, code, _) = await Handle(new ForbiddenException("INSUFICIENTE", "sin permiso"));

        status.Should().Be(StatusCodes.Status403Forbidden);
        code.Should().Be("INSUFICIENTE");
    }

    [Fact]
    public async Task Should_Map_CrossTenantViolationException_To_403()
    {
        var (status, code, _) = await Handle(new CrossTenantViolationException("Cliente", Guid.NewGuid(), Guid.NewGuid()));

        status.Should().Be(StatusCodes.Status403Forbidden);
        code.Should().Be("CROSS_EMPRESA_VIOLATION");
    }

    [Fact]
    public async Task Should_Map_UnauthorizedAccessException_To_401()
    {
        var (status, code, _) = await Handle(new UnauthorizedAccessException("token expirado"));

        status.Should().Be(StatusCodes.Status401Unauthorized);
        code.Should().Be("UNAUTHENTICATED");
    }

    [Fact]
    public async Task Should_Map_ValidationException_To_400_With_ErrorsArray()
    {
        var errors = new[]
        {
            new ValidationError("rfc", "RFC_INVALIDO", "Formato inválido"),
            new ValidationError("razonSocial", "REQUIRED", "La razón social es obligatoria"),
        };

        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/test";
        await handler.TryHandleAsync(ctx, new ValidationException(errors), CancellationToken.None);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        doc.RootElement.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        doc.RootElement.GetProperty("errores").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Should_Map_UnknownException_To_500_WithGenericDetail()
    {
        var (status, code, _) = await Handle(new InvalidProgramException("internal bug"));

        status.Should().Be(StatusCodes.Status500InternalServerError);
        code.Should().Be("INTERNAL_ERROR");
    }
}
