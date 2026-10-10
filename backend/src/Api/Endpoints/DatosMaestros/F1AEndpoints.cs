using MediatR;
using Millet.Api.Auth;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.Identidad.Domain;
using Millet.Api.Web;
namespace Millet.Api.Endpoints.DatosMaestros;

public static class F1AEndpoints
{
    public static IEndpointRouteBuilder MapF1AEndpoints(this IEndpointRouteBuilder app)
    {
        var clasificaciones = app.MapGroup("/api/v1/datos-maestros/clasificaciones-cliente").WithTags("DatosMaestros")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesGestionar);
        clasificaciones.MapGet("/",async(ISender sender,CancellationToken ct)=>Results.Ok(await sender.Send(new ListarClasificacionesClienteQuery(),ct)));
        clasificaciones.MapPost("/",async(GuardarClasificacionClienteCommand body,ISender sender,CancellationToken ct)=>Results.Ok(new { Id=await sender.Send(body with { Id=null },ct) }))
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        clasificaciones.MapPut("/{id:guid}",async(Guid id,GuardarClasificacionClienteCommand body,ISender sender,CancellationToken ct)=>Results.Ok(new { Id=await sender.Send(body with { Id=id },ct) }))
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        var ligados=app.MapGroup("/api/v1/datos-maestros/clientes/ligados-aw")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesGestionar);
        ligados.MapGet("/",async(ISender sender,CancellationToken ct)=>Results.Ok(await sender.Send(new ListarLigadosClienteQuery(),ct)));
        ligados.MapPost("/{id:guid}/revisar",async(Guid id,RevisarLigadoClienteCommand body,ISender sender,CancellationToken ct)=>
        { await sender.Send(body with { Id=id },ct); return Results.NoContent(); }).WithMetadata(new RequireIdempotencyKeyAttribute());
        var fiscal=app.MapGroup("/api/v1/datos-maestros/productos-aw/fiscal")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar);
        fiscal.MapGet("/reglas",async(ISender sender,CancellationToken ct)=>Results.Ok(await sender.Send(new ListarReglasFiscalesAwQuery(),ct)));
        fiscal.MapPost("/reglas",async(GuardarReglaFiscalAwCommand body,ISender sender,CancellationToken ct)=>Results.Ok(new { Id=await sender.Send(body with { Id=null },ct) }))
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        fiscal.MapPut("/reglas/{id:guid}",async(Guid id,GuardarReglaFiscalAwCommand body,ISender sender,CancellationToken ct)=>Results.Ok(new { Id=await sender.Send(body with { Id=id },ct) }))
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        fiscal.MapPost("/importar",async(ImportarFiscalAwCommand body,ISender sender,CancellationToken ct)=>Results.Ok(await sender.Send(body,ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        fiscal.MapPost("/{id:guid}/acciones",async(Guid id,AccionFiscalProductoCommand body,ISender sender,CancellationToken ct)=>
        { await sender.Send(body with { Id=id },ct); return Results.NoContent(); }).WithMetadata(new RequireIdempotencyKeyAttribute());
        fiscal.MapGet("/{tipoEntidad}/{id:guid}/historial",async(Guid id,string tipoEntidad,ISender sender,CancellationToken ct)=>Results.Ok(await sender.Send(new HistorialFiscalAwQuery(id,tipoEntidad),ct)));
        return app;
    }
}
