using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// C1.4 · Motor de pólizas desde eventos: roles de cuenta (Laura los mantiene), plantillas (consulta), bandeja de rechazos
/// (reprocesar y descartar con motivo) y, solo fuera de producción, la entrada manual de un evento para pruebas (los listeners
/// llegan en C1.5).
/// </summary>
public static class ContabilidadMotorEndpoints
{
    public sealed record CrearRolRequest(string Codigo, string Descripcion, Guid? CuentaId, Guid? SucursalId, string? Moneda, DateOnly? VigenteDesde);
    public sealed record CambiarCuentaRolRequest(Guid CuentaId, string? Motivo);
    public sealed record DescartarRequest(string Motivo);
    public sealed record ProcesarEventoRequest(string Tipo, Guid EventoId, JsonElement Payload);

    public static IEndpointRouteBuilder MapContabilidadMotorEndpoints(this IEndpointRouteBuilder app, IHostEnvironment env)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadMotorLeer;
        var roles = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadRolesCuentaAdministrar;
        var rechazos = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadRechazosGestionar;

        var g = app.MapGroup("/api/v1/contabilidad/motor").WithTags("Contabilidad · Motor de pólizas").RequireAuthorization();

        g.MapGet("/roles", async (IMediator m, CancellationToken ct) => Results.Ok(await m.Send(new ListarRolesCuentaQuery(), ct)))
            .RequireAuthorization(leer).WithName("ListarRolesCuenta").Produces<IReadOnlyList<RolCuentaResponse>>();

        g.MapPost("/roles", async (CrearRolRequest b, HttpResponse response, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new CrearRolCuentaCommand(b.Codigo, b.Descripcion, b.CuentaId, b.SucursalId, b.Moneda, b.VigenteDesde), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Created($"/api/v1/contabilidad/motor/roles/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(roles).WithName("CrearRolCuenta").Produces<RolCuentaResponse>(StatusCodes.Status201Created);

        g.MapPut("/roles/{id:guid}/cuenta", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CambiarCuentaRolRequest b,
            HttpResponse response, IMediator m, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await m.Send(new CambiarCuentaRolCommand(id, b.CuentaId, b.Motivo, version), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(roles).WithName("CambiarCuentaRol")
        .WithSummary("Cambia la cuenta de un rol (solo pólizas nuevas; queda en bitácora)")
        .Produces<RolCuentaResponse>().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapGet("/roles/{id:guid}/bitacora", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new BitacoraRolCuentaQuery(id), ct)))
            .RequireAuthorization(leer).WithName("BitacoraRolCuenta").Produces<IReadOnlyList<RolCuentaCambioResponse>>();

        g.MapGet("/plantillas", async (IMediator m, CancellationToken ct) => Results.Ok(await m.Send(new ListarPlantillasContablesQuery(), ct)))
            .RequireAuthorization(leer).WithName("ListarPlantillasContables").Produces<IReadOnlyList<PlantillaContableResponse>>();

        g.MapGet("/rechazos", async ([FromQuery] EstadoRechazoContable? estado, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ListarRechazosContablesQuery(estado), ct)))
            .RequireAuthorization(leer).WithName("ListarRechazosContables").Produces<IReadOnlyList<RechazoContableResponse>>();

        g.MapPost("/rechazos/{id:guid}/reprocesar", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ReprocesarRechazoCommand(id), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(rechazos).WithName("ReprocesarRechazoContable")
            .WithSummary("Vuelve a correr la plantilla vigente con el mismo payload y la misma llave")
            .Produces<ResultadoMotor>().ProducesProblem(StatusCodes.Status409Conflict);

        g.MapPost("/rechazos/{id:guid}/descartar", async (Guid id, DescartarRequest b, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new DescartarRechazoCommand(id, b.Motivo), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(rechazos).WithName("DescartarRechazoContable")
            .Produces<RechazoContableResponse>().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Entrada manual de un evento: solo en ambientes de prueba (decisión del 117, 26-oct); en producción no existe la ruta.
        if (!env.IsProduction())
        {
            g.MapPost("/eventos", async (ProcesarEventoRequest b, IMediator m, CancellationToken ct) =>
                Results.Ok(await m.Send(new ProcesarEventoContableCommand(b.Tipo, b.EventoId, b.Payload.GetRawText()), ct)))
                .WithMetadata(new RequireIdempotencyKeyAttribute())
                .RequireAuthorization(rechazos).WithName("ProcesarEventoContable")
                .WithSummary("[Solo pruebas] Procesa un evento con el motor como lo hará un listener de C1.5")
                .Produces<ResultadoMotor>();
        }
        return app;
    }
}
