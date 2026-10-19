using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// ADM-03: deja en la bitácora quién fue rechazado con 403 (policy de permiso o alcance por sucursal), a qué ruta
/// y por qué. Solo usuarios autenticados (el 401 no pasa por aquí) y nunca rompe la respuesta: si la escritura falla
/// se registra en log y el 403 sale igual. La ruta no incluye query string.
/// </summary>
public static class AccesoDenegadoAudit
{
    public static async Task RegistrarAsync(HttpContext context, string motivo, string? permiso, CancellationToken ct)
    {
        try
        {
            var user = context.RequestServices.GetRequiredService<ICurrentUserContext>();
            if (user.UserId is not Guid userId) return;
            var empresa = context.RequestServices.GetRequiredService<ICurrentEmpresaContext>();
            var ruta = $"{context.Request.Method} {context.Request.Path}";
            await context.RequestServices.GetRequiredService<AuthAccessAuditWriter>().WriteAsync(
                "acceso-denegado", userId, empresa.Current, "api", motivo, context.Connection.RemoteIpAddress, ct,
                actorNombre: user.UserName, actorEmail: user.Email,
                resumen: $"Acceso denegado a {ruta} ({motivo})", ruta: ruta, permiso: permiso);
        }
        catch (Exception ex)
        {
            // Sin RequestServices (contexto armado a mano) no hay logger; la auditoría nunca debe tumbar la respuesta 403.
            context.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger("AccesoDenegadoAudit")
                .LogWarning(ex, "No se pudo auditar el acceso denegado a {Path}", context.Request.Path);
        }
    }
}
