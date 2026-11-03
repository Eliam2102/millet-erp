using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.CuentasPorPagar.Application.Portal;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

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

    private const string Portal = "/api/v1/portal-proveedores";

    private static async Task<byte[]> LeerAsync(IFormFile f, CancellationToken ct)
    {
        await using var s = f.OpenReadStream();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

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

        // Subir factura (I) o complemento de pago (P). El proveedor sale del token, nunca del cliente.
        portal.MapPost("/cfdi", async (IFormFile xml, IFormFile? pdf, HttpContext http, IMediator mediator, CancellationToken ct) =>
        {
            if (xml is null || !xml.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("PORTAL_ARCHIVO_NO_XML", "Sube el CFDI en un archivo .xml.");
            if (pdf is { Length: > 0 } && !pdf.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("PORTAL_ARCHIVO_NO_PDF", "El archivo adjunto debe ser un .pdf.");
            if (xml.Length > SubirCfdiPortalHandler.MaxXmlBytes || pdf?.Length > SubirCfdiPortalHandler.MaxPdfBytes)
                throw new BusinessRuleException("PORTAL_ARCHIVO_MUY_GRANDE", "Un archivo excede el tamaño máximo permitido (XML 2 MB, PDF 10 MB).");

            var r = await mediator.Send(new SubirCfdiPortalCommand(http.User.ProveedorId()!.Value, await LeerAsync(xml, ct), pdf is { Length: > 0 } ? await LeerAsync(pdf, ct) : null), ct);
            return r.Resultado == "Recibido" ? Results.Created($"{Portal}/cfdi", r) : Results.Ok(r);
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(13 * 1024 * 1024))
        .RequireAuthorization(PortalProveedorAuth.Policy)
        .WithName("PortalProveedorSubirCfdi")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<SubirCfdiPortalResponse>(StatusCodes.Status201Created)
        .Produces<SubirCfdiPortalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
