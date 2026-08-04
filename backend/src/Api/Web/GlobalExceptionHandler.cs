using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Idempotency;
using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Api.Web;

/// <summary>
/// Maneja todas las excepciones no controladas y las traduce a
/// Problem Details (RFC 7807) con extensiones del proyecto: <c>traceId</c>,
/// <c>code</c>, y <c>errores[]</c> cuando aplica. Las excepciones de dominio
/// mapean a códigos HTTP específicos; cualquier excepción no manejada cae a
/// 500 con detalle genérico al usuario y stack completo en log.
/// Ver ADR-0010 y ADR-0018.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = MapToProblemDetails(exception, httpContext);

        if (problem.Status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception: {ExceptionType}", exception.GetType().Name);
        }
        else
        {
            _logger.LogInformation(
                "Domain exception: {ExceptionType} {Code} -> HTTP {Status}",
                exception.GetType().Name,
                problem.Extensions.TryGetValue("code", out var code) ? code : "(none)",
                problem.Status);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body, problem, JsonOptions, cancellationToken);

        return true;
    }

    private static ProblemDetails MapToProblemDetails(Exception exception, HttpContext httpContext)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        return exception switch
        {
            ValidationException ve => CreateValidationProblem(ve, httpContext, traceId),
            BusinessRuleException bre => CreateProblem(bre.Code, bre.Message, StatusCodes.Status422UnprocessableEntity, httpContext, traceId),
            EntityNotFoundException enfe => CreateProblem(enfe.Code, enfe.Message, StatusCodes.Status404NotFound, httpContext, traceId),
            ConcurrencyException ce => CreateProblem(ce.Code, ce.Message, StatusCodes.Status409Conflict, httpContext, traceId),
            ConflictException cfe => CreateProblem(cfe.Code, cfe.Message, StatusCodes.Status409Conflict, httpContext, traceId),
            ForbiddenException fe => CreateProblem(fe.Code, fe.Message, StatusCodes.Status403Forbidden, httpContext, traceId),
            CrossTenantViolationException cte => CreateProblem(cte.Code, cte.Message, StatusCodes.Status403Forbidden, httpContext, traceId),
            MissingEmpresaContextException mece => CreateProblem(mece.Code, mece.Message, StatusCodes.Status500InternalServerError, httpContext, traceId),
            IdempotencyKeyMissingException ikm => CreateProblem(ikm.Code, ikm.Message, StatusCodes.Status400BadRequest, httpContext, traceId),
            IdempotencyKeyInvalidException iki => CreateProblem(iki.Code, iki.Message, StatusCodes.Status400BadRequest, httpContext, traceId),
            IdempotencyInProgressException iip => CreateProblem(iip.Code, iip.Message, StatusCodes.Status409Conflict, httpContext, traceId),
            IdempotencyBodyMismatchException ibm => CreateProblem(ibm.Code, ibm.Message, StatusCodes.Status422UnprocessableEntity, httpContext, traceId),
            UnauthorizedAccessException => CreateProblem("UNAUTHENTICATED", "Sesión inválida o expirada.", StatusCodes.Status401Unauthorized, httpContext, traceId),
            // Dependencia externa (FiscalAPI) apagada o caída — el FE degrada a captura manual (FAC-DET-PR1).
            Millet.Integraciones.Fiscal.Domain.Exceptions.CatalogoSatNoDisponibleException cse
                => CreateProblem(cse.Code, cse.Message, StatusCodes.Status503ServiceUnavailable, httpContext, traceId),
            DomainException de => CreateProblem(de.Code, de.Message, StatusCodes.Status400BadRequest, httpContext, traceId),
            _ => CreateProblem(
                "INTERNAL_ERROR",
                "Ocurrió un error inesperado. Reporta el código de seguimiento traceId a soporte.",
                StatusCodes.Status500InternalServerError,
                httpContext,
                traceId),
        };
    }

    private static ProblemDetails CreateProblem(
        string code, string message, int status, HttpContext httpContext, string traceId)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://millet-erp/errors/{code.ToLowerInvariant()}",
            Title = message,
            Status = status,
            Detail = message,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["code"] = code;
        return problem;
    }

    private static ProblemDetails CreateValidationProblem(
        ValidationException exception, HttpContext httpContext, string traceId)
    {
        var problem = new ProblemDetails
        {
            Type = "https://millet-erp/errors/validation",
            Title = "Errores de validación",
            Status = StatusCodes.Status400BadRequest,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["code"] = exception.Code;
        problem.Extensions["errores"] = exception.Errors;
        return problem;
    }
}
