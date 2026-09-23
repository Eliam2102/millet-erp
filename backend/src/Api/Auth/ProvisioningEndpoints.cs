using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth.Provisioning;
using Millet.SharedKernel.Application;
using Millet.Compartido.Application.Ports;
using Millet.Identidad.Application.Ports;

namespace Millet.Api.Auth;

public static class ProvisioningEndpoints
{
    public static IEndpointRouteBuilder MapProvisioningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identidad/provisionar").WithTags("Identidad (Provisioning)");

        group.MapPost("/", async (
            [FromBody] ProvisionarUsuarioRequest request,
            IEntraProvisioningService provisioningService,
            CancellationToken cancellationToken) =>
        {
            ProvisioningResult result;

            if (request.EsNuevo)
            {
                result = await provisioningService.CrearNuevoUsuarioAsync(
                    request.Nombre, 
                    request.Apellido, 
                    request.Upn, 
                    request.CorreoContacto, 
                    cancellationToken);
            }
            else
            {
                result = await provisioningService.VincularUsuarioExistenteAsync(
                    request.Upn, 
                    request.CorreoContacto, 
                    cancellationToken);
            }

            if (!result.Success)
            {
                return Results.BadRequest(new { mensaje = result.Mensaje });
            }

            return Results.Ok(new ProvisionarUsuarioResponse(
                Success: true, 
                EntraOid: result.EntraOid!, 
                Mensaje: result.Mensaje!));
        })
        .RequireAuthorization()
        .WithName("PostProvisionarUsuario");
        // Nota: Agrega RequiresPermission("identidad.usuarios.crear") si el policy está configurado
        // .RequireAuthorization(policy => policy.RequireClaim("Permission", "identidad.usuarios.crear")) 
        // pero usaremos el require standard.

        group.MapGet("/verificar-upn", async ([FromQuery] string upn, IEntraProvisioningService provisioningService, CancellationToken cancellationToken) => { if (string.IsNullOrWhiteSpace(upn)) { return Results.BadRequest(new { mensaje = "El parámetro upn es requerido." }); } var existe = await provisioningService.ExisteUsuarioAsync(upn, cancellationToken); return Results.Ok(new { existe }); }).RequireAuthorization().WithName("GetVerificarUpn"); return app;
    }
}

public record ProvisionarUsuarioRequest(
    string Nombre,
    string Apellido,
    string Upn,
    string CorreoContacto,
    bool EsNuevo
);

public record ProvisionarUsuarioResponse(
    bool Success,
    string EntraOid,
    string Mensaje
);
