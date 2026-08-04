using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Millet.Api.Web;

/// <summary>
/// Serializer custom para <c>/health</c> con detalle por dependencia. No se
/// usa el writer por defecto de <c>HealthChecks.UI.Client</c> porque es verbose
/// y no controlamos su shape. Vive en el proyecto Api porque depende de
/// <c>HttpContext</c> (AspNetCore framework). Ver ADR-0019.
/// </summary>
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static Task WriteDetailedAsync(HttpContext httpContext, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(report);

        httpContext.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = (int)e.Value.Duration.TotalMilliseconds,
                tags = e.Value.Tags,
                description = e.Value.Description,
                data = e.Value.Data,
            }),
        };

        return JsonSerializer.SerializeAsync(httpContext.Response.Body, payload, JsonOptions);
    }
}
