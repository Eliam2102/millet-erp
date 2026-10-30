using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Api.Web;
using Millet.SharedKernel.Application.Idempotency;

namespace Millet.Api.UnitTests;

public sealed class GlobalExceptionHandlerRetryAfterTests
{
    [Fact]
    public async Task Idempotency_en_proceso_responde_409_con_Retry_After()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, new IdempotencyInProgressException(7), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("7", context.Response.Headers.RetryAfter.ToString());
    }
}
