using Microsoft.AspNetCore.Http;
using Millet.SharedKernel.Application;
using Serilog.Context;

namespace Millet.SharedKernel.Infrastructure.Logging;

/// <summary>
/// Middleware que pushea <c>EmpresaId</c> y <c>UsuarioId</c> al
/// <c>LogContext</c> de Serilog durante el scope del request, para que
/// todos los logs (incluidos los del handler MediatR y EF Core) los
/// incluyan como custom dimensions en Application Insights (F8-PR2,
/// ADR-0006). El <c>TraceId</c>/<c>SpanId</c> ya viene de OpenTelemetry.
///
/// <para>
/// Se registra después de <c>UseAuthentication/UseAuthorization</c> para
/// que los contexts ya estén poblados con claims del JWT. Si no hay
/// usuario o empresa (endpoints anónimos), el push no se hace para esa
/// propiedad — los logs simplemente la omiten.
/// </para>
/// </summary>
public sealed class RequestContextLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestContextLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentEmpresaContext empresaContext,
        ICurrentUserContext userContext)
    {
        var disposables = new List<IDisposable>(2);
        try
        {
            if (empresaContext.Current is Guid empresaId)
            {
                disposables.Add(LogContext.PushProperty("EmpresaId", empresaId));
            }
            if (userContext.UserId is Guid usuarioId)
            {
                disposables.Add(LogContext.PushProperty("UsuarioId", usuarioId));
            }

            await _next(context);
        }
        finally
        {
            foreach (var d in disposables)
            {
                d.Dispose();
            }
        }
    }
}
