using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Avisos de Almacén dentro del ERP (86e3tac2y): cada usuario lee y marca solo los suyos; no hay permiso aparte porque el
/// destinatario ya se decidió al generar el aviso (rol y sucursal).
/// </summary>
public static class AvisosAlmacenEndpoints
{
    public sealed record AvisoAlmacenResponse(Guid Id, string Tipo, string Titulo, string Mensaje, string? Ruta, DateTimeOffset CreadoAt, bool Leido);

    public static IEndpointRouteBuilder MapAvisosAlmacenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/avisos").WithTags("Almacen").RequireAuthorization();

        group.MapGet("/", async (AlmacenDbContext db, ICurrentUserContext user, CancellationToken ct) =>
        {
            var uid = user.UserId ?? throw new BusinessRuleException("AVISOS_SIN_USUARIO", "Se requiere usuario autenticado.");
            var filas = await db.AvisosAlmacen.AsNoTracking().Where(a => a.UsuarioDestinoId == uid)
                .OrderByDescending(a => a.CreadoAt).Take(30).ToListAsync(ct);
            return Results.Ok(filas.Select(a => new AvisoAlmacenResponse(a.Id, a.Tipo, a.Titulo, a.Mensaje, a.Ruta, a.CreadoAt, a.LeidoAt is not null)).ToList());
        })
        .WithName("ListarAvisosAlmacen")
        .Produces<List<AvisoAlmacenResponse>>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/leer", async (Guid id, AlmacenDbContext db, ICurrentUserContext user, CancellationToken ct) =>
        {
            var uid = user.UserId ?? throw new BusinessRuleException("AVISOS_SIN_USUARIO", "Se requiere usuario autenticado.");
            var aviso = await db.AvisosAlmacen.FirstOrDefaultAsync(a => a.Id == id && a.UsuarioDestinoId == uid, ct);
            if (aviso is null) return Results.NotFound();
            aviso.MarcarLeido();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("MarcarAvisoAlmacenLeido")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
