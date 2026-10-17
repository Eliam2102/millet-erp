using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Api.Endpoints.DatosMaestros;

/// <summary>Matriz campo → dueño y bitácora de cambios de la sincronización con A+W (ADM-06).</summary>
public sealed record ResolverConflictoBody(string Resolucion, int Version);

public static class SincronizacionAwEndpoints
{
    public static IEndpointRouteBuilder MapSincronizacionAwEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/datos-maestros/sincronizacion-aw/matriz",
                () => Results.Ok(new { version = MatrizCampoDuenoAw.Version, campos = MatrizCampoDuenoAw.Campos }))
            .WithTags("DatosMaestros").RequireAuthorization();

        app.MapGet("/api/v1/datos-maestros/clientes/{id:guid}/cambios-aw",
                async (Guid id, int? limit, CompartidoDbContext db, ISender sender, CancellationToken ct) =>
                {
                    if (!await db.Clientes.AsNoTracking().AnyAsync(c => c.Id == id, ct))
                        throw new EntityNotFoundException("CLIENTE_NO_ENCONTRADO", $"No existe cliente con id '{id}'.");
                    return Results.Ok(await sender.Send(new ListarCambiosMaestroAwQuery("Cliente", id, Limit: limit ?? 50), ct));
                })
            .WithTags("DatosMaestros")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesOrigenVer);

        var conflictos = app.MapGroup("/api/v1/datos-maestros/clientes/conflictos-aw").WithTags("DatosMaestros")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesSincronizar);
        conflictos.MapGet("/", async (string? estado, Guid? ejecucionId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListarCambiosMaestroAwQuery("Cliente", Estado: estado ?? "Pendiente",
                EjecucionId: ejecucionId, Limit: 200, SoloConflictos: true), ct)));
        conflictos.MapPost("/{id:guid}/resolver", async (Guid id, ResolverConflictoBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ResolverConflictoMaestroAwCommand(id, "Cliente", body.Resolucion, body.Version), ct);
            return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute());
        return app;
    }
}
