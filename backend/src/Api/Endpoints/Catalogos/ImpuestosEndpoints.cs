using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Catalogos;

/// <summary>Catálogo de referencia de impuestos; no calcula impuestos.</summary>
public static class ImpuestosEndpoints
{
    public sealed record ImpuestoItem(Guid Id, string Clave, string Nombre, string Tipo,
        string Factor, decimal Tasa, DateOnly VigenteDesde, DateOnly? VigenteHasta,
        bool Activo, string Fuente, int Version);

    public sealed record CrearImpuestoPayload(string Clave, string Nombre, string Tipo,
        string Factor, decimal Tasa, DateOnly VigenteDesde, DateOnly? VigenteHasta,
        bool Activo, string Fuente);

    public sealed record ActualizarImpuestoPayload(string Nombre, DateOnly? VigenteHasta,
        bool Activo, string Fuente);

    public static IEndpointRouteBuilder MapImpuestosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos/impuestos").WithTags("Catalogos");

        group.MapGet("/", async (
            [FromQuery] DateOnly? fecha,
            [FromQuery] bool? incluirHistorico,
            CompartidoDbContext db,
            IClock clock,
            CancellationToken ct) =>
        {
            var dia = fecha ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            var query = db.ImpuestosReferencia.AsNoTracking();
            if (incluirHistorico != true)
                query = query.Where(i => i.Activo && i.VigenteDesde <= dia &&
                    (i.VigenteHasta == null || i.VigenteHasta >= dia));
            var items = await query.OrderBy(i => i.Clave).ThenByDescending(i => i.VigenteDesde)
                .Select(i => new ImpuestoItem(i.Id, i.Clave, i.Nombre, i.Tipo, i.Factor,
                    i.Tasa, i.VigenteDesde, i.VigenteHasta, i.Activo, i.Fuente, i.Version))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarImpuestosReferencia")
        .Produces<IReadOnlyList<ImpuestoItem>>();

        group.MapGet("/{id:guid}", async (Guid id, CompartidoDbContext db, CancellationToken ct) =>
        {
            var item = await db.ImpuestosReferencia.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id, ct)
                ?? throw new EntityNotFoundException("IMPUESTO_NO_ENCONTRADO",
                    "No existe el impuesto solicitado.");
            return Results.Ok(ToItem(item));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ConsultarImpuestoReferencia")
        .Produces<ImpuestoItem>();

        group.MapPost("/", async (
            [FromBody] CrearImpuestoPayload payload,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            var item = new ImpuestoReferencia(Guid.CreateVersion7(), payload.Clave,
                payload.Nombre, payload.Tipo, payload.Factor, payload.Tasa,
                payload.VigenteDesde, payload.VigenteHasta, payload.Activo, payload.Fuente);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7390004)", ct);
            await EnsureNoOverlapAsync(db, item, null, ct);
            db.ImpuestosReferencia.Add(item);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/catalogos/impuestos/{item.Id}", ToItem(item));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("CrearImpuestoReferencia")
        .Produces<ImpuestoItem>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarImpuestoPayload payload,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7390004)", ct);
            var item = await db.ImpuestosReferencia.FirstOrDefaultAsync(i => i.Id == id, ct)
                ?? throw new EntityNotFoundException("IMPUESTO_NO_ENCONTRADO", "No existe el impuesto solicitado.");
            item.Actualizar(payload.Nombre, payload.VigenteHasta, payload.Activo, payload.Fuente);
            await EnsureNoOverlapAsync(db, item, id, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(ToItem(item));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar)
        .WithName("ActualizarImpuestoReferencia")
        .Produces<ImpuestoItem>()
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task EnsureNoOverlapAsync(CompartidoDbContext db,
        ImpuestoReferencia item, Guid? exceptId, CancellationToken ct)
    {
        var end = item.VigenteHasta ?? DateOnly.MaxValue;
        if (await db.ImpuestosReferencia.AsNoTracking().AnyAsync(i =>
            i.Id != exceptId && i.Clave == item.Clave && i.Tipo == item.Tipo &&
            i.Factor == item.Factor && i.VigenteDesde <= end &&
            (i.VigenteHasta == null || i.VigenteHasta >= item.VigenteDesde), ct))
            throw new ConflictException("IMPUESTO_VIGENCIA_DUPLICADA",
                "Ya existe un registro para la misma clave, tipo y factor con vigencia superpuesta.");
    }

    private static ImpuestoItem ToItem(ImpuestoReferencia i) => new(i.Id, i.Clave,
        i.Nombre, i.Tipo, i.Factor, i.Tasa, i.VigenteDesde, i.VigenteHasta,
        i.Activo, i.Fuente, i.Version);
}
