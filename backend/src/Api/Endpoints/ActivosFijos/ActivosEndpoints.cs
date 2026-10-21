using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Identidad.Domain;
namespace Millet.Api.Endpoints.ActivosFijos;
public static class ActivosEndpoints
{
    public static IEndpointRouteBuilder MapActivosFijos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/activos-fijos").WithTags("Activos fijos").RequireAuthorization();
        g.MapGet("/clases", async (ActivosFijosDbContext db, Millet.SharedKernel.Application.ICurrentUserPermissions permisos, CancellationToken ct) =>
        {
            foreach (var p in new[] { PermisosCanonicos.ActivosFijosActivosVer, PermisosCanonicos.ActivosFijosClasesAdministrar, PermisosCanonicos.ComprasRequisicionesCrear, PermisosCanonicos.ComprasRequisicionesEditar, PermisosCanonicos.ComprasOrdenesCrear, PermisosCanonicos.ComprasOrdenesCrearSinRq })
                if (await permisos.TieneAsync(p, ct)) return Results.Ok(await db.Clases.AsNoTracking().OrderBy(x => x.Clave).ToListAsync(ct));
            return Results.Forbid();
        });
        g.MapGet("/documentos-proveedor", async (Guid activoId, bool notaCredito, string? q, ActivoServicio servicio, Millet.CuentasPorPagar.Application.PublicPorts.ICapitalizacionProveedorReadPort documentos, CancellationToken ct) =>
        {
            var activo = await servicio.CargarAsync(activoId, true, ct);
            return Results.Ok(await documentos.BuscarAsync(notaCredito, activo.FacturaProveedorId, activo.SucursalId, q, ct));
        }).RequireAuthorization(Policy(PermisosCanonicos.ActivosFijosCapitalizar));
        g.MapGet("/equipos", async (string? q, IMediator m, CancellationToken ct) => Results.Ok(await m.Send(new Millet.CentrosCosto.Application.Catalogo.BuscarDim3AbiertoQuery(q, false, 50), ct)))
            .RequireAuthorization(Policy(PermisosCanonicos.ActivosFijosActivosVer));
        g.MapGet("/centros", async (string? q, Millet.CentrosCosto.Application.PublicPorts.ICentroCostoCapturaPort centros, CancellationToken ct) => Results.Ok(await centros.BuscarAsync(q, true, ct)))
            .RequireAuthorization(Policy(PermisosCanonicos.ActivosFijosActivosVer));
        g.MapPost("/clases", async (ClaseActivoDatos datos, IMediator m, CancellationToken ct) => Results.Ok(await m.Send(new GuardarClaseCommand(null, null, datos), ct)))
            .RequireAuthorization(Policy(PermisosCanonicos.ActivosFijosClasesAdministrar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapPut("/clases/{id:guid}", async (Guid id, ClaseActivoDatos datos, HttpRequest request, IMediator m, CancellationToken ct) =>
            Version(request) is int v ? Results.Ok(await m.Send(new GuardarClaseCommand(id, v, datos), ct)) : FaltaVersion())
            .RequireAuthorization(Policy(PermisosCanonicos.ActivosFijosClasesAdministrar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapGet("/activos", async ([Microsoft.AspNetCore.Http.AsParameters] FiltroActivos f, IMediator m, CancellationToken ct) => Results.Ok(await m.Send(new ListarActivosQuery(f.ClaseId, f.SucursalId, f.Estado, f.ResponsableId, f.EquipoId, f.Offset ?? 0, f.Limit ?? 10), ct)))
            .RequireAuthorization(Policy(PermisosCanonicos.ActivosFijosActivosVer));
        g.MapGet("/activos/{id:guid}", async (Guid id, IMediator m, HttpResponse response, CancellationToken ct) => Respuesta(await m.Send(new ObtenerActivoQuery(id), ct), response))
            .RequireAuthorization(Policy(PermisosCanonicos.ActivosFijosActivosVer));
        g.MapPost("/activos", async (CrearActivoCommand datos, IMediator m, HttpResponse response, CancellationToken ct) => Respuesta(await m.Send(datos, ct), response))
            .RequireAuthorization(Policy(PermisosCanonicos.ActivosFijosActivosGestionar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        foreach (var accion in new[] { "editar", "capitalizar", "mejora", "nota-credito", "trasladar", "aceptar" })
        {
            var permiso = accion is "capitalizar" or "mejora" or "nota-credito" ? PermisosCanonicos.ActivosFijosCapitalizar
                : accion is "trasladar" or "aceptar" ? PermisosCanonicos.ActivosFijosTrasladosGestionar : PermisosCanonicos.ActivosFijosActivosGestionar;
            g.MapPost("/activos/{id:guid}/" + accion, async (Guid id, MutacionActivo datos, HttpRequest request, HttpResponse response, IMediator m, CancellationToken ct) =>
                Version(request) is int v ? Respuesta(await m.Send(new MutarActivoCommand(id, v, accion, datos.Datos, datos.Destino, datos.TrasladoId, datos.Ajuste), ct), response) : FaltaVersion())
                .RequireAuthorization(Policy(permiso)).WithMetadata(new RequireIdempotencyKeyAttribute());
        }
        return app;
    }
    private static string Policy(string p) => PermissionPolicyProvider.Prefix + p;
    private static int? Version(HttpRequest request) => int.TryParse(request.Headers.IfMatch.ToString().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    private static IResult FaltaVersion() => Results.Problem(statusCode: 428, title: "Envíe If-Match con el ETag vigente.");
    private static IResult Respuesta(Activo a, HttpResponse response) { response.Headers.ETag = $"\"{a.Version}\""; return Results.Ok(a); }
    public sealed record MutacionActivo(DatosActivo? Datos = null, CustodiaActivo? Destino = null, Guid? TrasladoId = null, AjusteActivoDatos? Ajuste = null);
    public sealed record FiltroActivos(Guid? ClaseId, Guid? SucursalId, EstadoActivo? Estado, Guid? ResponsableId, Guid? EquipoId, int? Offset, int? Limit);
}
