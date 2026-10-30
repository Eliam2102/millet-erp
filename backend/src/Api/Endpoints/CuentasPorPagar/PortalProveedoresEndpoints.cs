using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.CuentasPorPagar.Application.Portal;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Portal de proveedores (P8.3, parte A). Entrada aparte del ERP: código por correo, token propio
/// (audiencia <see cref="PortalProveedorAuth.Audience"/>) y solo datos del proveedor del token.
/// Cada endpoint autenticado exige la política del esquema del portal; el esquema por defecto del ERP
/// nunca los atiende. La administración de correos autorizados sí es del ERP (permiso de CxP).
/// </summary>
public static class PortalProveedoresEndpoints
{
    public sealed record SolicitarCodigoRequest(string? Email);
    public sealed record IniciarSesionRequest(string? Email, string? Codigo);
    public sealed record AgregarAccesoRequest(string? Email);
    public sealed record CambiarActivoRequest(bool Activo);

    public static IEndpointRouteBuilder MapPortalProveedoresEndpoints(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/api/v1/portal-proveedores").WithTags("PortalProveedores");

        portal.MapPost("/codigo", async (SolicitarCodigoRequest request, IPortalProveedorAutenticacion auth, CancellationToken ct) =>
        {
            await auth.SolicitarCodigoAsync(request.Email, ct);
            return Results.Accepted(); // siempre 202: no revela si el correo está autorizado
        })
        .AllowAnonymous()
        .WithName("PortalProveedorSolicitarCodigo")
        .Produces(StatusCodes.Status202Accepted);

        portal.MapPost("/sesion", async (IniciarSesionRequest request, IPortalProveedorAutenticacion auth, CancellationToken ct) =>
        {
            var token = await auth.IniciarSesionAsync(request.Email, request.Codigo, ct);
            return token is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Código inválido",
                    detail: "El correo o el código no son válidos, o el código venció. Solicita uno nuevo.",
                    extensions: new Dictionary<string, object?> { ["code"] = "PORTAL_CODIGO_INVALIDO" })
                : Results.Ok(token);
        })
        .AllowAnonymous()
        .WithName("PortalProveedorIniciarSesion")
        .Produces<PortalToken>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        portal.MapGet("/facturas", async (HttpContext http, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarFacturasPortalQuery(http.User.ProveedorId()!.Value), ct)))
        .RequireAuthorization(PortalProveedorAuth.Policy)
        .WithName("PortalProveedorListarFacturas");

        portal.MapGet("/facturas/{id:guid}", async (Guid id, HttpContext http, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerFacturaPortalQuery(http.User.ProveedorId()!.Value, id), ct)))
        .RequireAuthorization(PortalProveedorAuth.Policy)
        .WithName("PortalProveedorObtenerFactura")
        .ProducesProblem(StatusCodes.Status404NotFound);

        portal.MapGet("/pagos", async (HttpContext http, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarPagosPortalQuery(http.User.ProveedorId()!.Value), ct)))
        .RequireAuthorization(PortalProveedorAuth.Policy)
        .WithName("PortalProveedorListarPagos");

        // Ficha del proveedor, sección «Acceso al portal» (ERP, esquema por defecto).
        var accesos = app.MapGroup("/api/v1/cuentas-por-pagar/proveedores/{proveedorId:guid}/acceso-portal")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarPortalAdministrar);

        accesos.MapGet("/", async (Guid proveedorId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarAccesosPortalQuery(proveedorId), ct)))
        .WithName("ListarAccesosPortalProveedor");

        accesos.MapPost("/", async (Guid proveedorId, [FromBody] AgregarAccesoRequest request, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AgregarAccesoPortalCommand(proveedorId, request.Email ?? string.Empty), ct)))
        .WithName("AgregarAccesoPortalProveedor");

        accesos.MapPut("/{accesoId:guid}/activo", async (Guid proveedorId, Guid accesoId, [FromBody] CambiarActivoRequest request, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CambiarActivoAccesoPortalCommand(proveedorId, accesoId, request.Activo), ct)))
        .WithName("CambiarActivoAccesoPortalProveedor");

        return app;
    }
}
