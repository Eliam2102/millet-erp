using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Asignaciones;

// ============================================================================
// Commands de la asignación artículo→ubicación (OITW, ADR-0047 PR3).
//
// Asignar: crea (o reactiva) la asignación Y crea la fila de saldo en 0 para
// (ubicacion, articulo) de forma idempotente (ON CONFLICT DO NOTHING, mismo
// target que el trigger de saldos) — todo en UNA transacción. Si un movimiento
// ya creó la fila, no la pisa.
//
// Desasignar: guardrail EN_USO (bloquea si hay saldo > 0); si pasa, desactiva la
// asignación Y borra la fila-en-0 en la misma transacción.
// ============================================================================

public sealed record AsignacionResponse(
    Guid Id,
    Guid UbicacionId,
    Guid ArticuloId,
    EstatusCatalogo Estatus);

// ─── Asignar (crear/reactivar asignación + fila-en-0) ─────────────────────────

public sealed record AsignarArticuloAUbicacionCommand(
    Guid UbicacionId,
    Guid ArticuloId) : IRequest<AsignacionResponse>;

public sealed class AsignarArticuloAUbicacionValidator
    : AbstractValidator<AsignarArticuloAUbicacionCommand>
{
    public AsignarArticuloAUbicacionValidator()
    {
        RuleFor(c => c.UbicacionId).NotEqual(Guid.Empty);
        RuleFor(c => c.ArticuloId).NotEqual(Guid.Empty);
    }
}

public sealed class AsignarArticuloAUbicacionHandler
    : IRequestHandler<AsignarArticuloAUbicacionCommand, AsignacionResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public AsignarArticuloAUbicacionHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<AsignacionResponse> Handle(
        AsignarArticuloAUbicacionCommand request, CancellationToken cancellationToken)
    {
        // Ubicación debe existir (y de ahí sale el sub_almacen_id denormalizado).
        var ubicacion = await _db.Ubicaciones.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UbicacionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "UBICACION_NO_ENCONTRADA",
                $"No existe ubicación con id '{request.UbicacionId}'.");

        // La ÚNICA (es_default) solo se drena; no admite asignaciones. Así el
        // modelo de asignaciones contiene únicamente racks reales y ninguna
        // entrada puede enrutar a la ÚNICA por la vía de la asignación (C7.2b).
        if (ubicacion.EsDefault)
            throw new BusinessRuleException(
                "ASIGNACION_A_UNICA",
                "No se puede asignar un artículo a la ubicación ÚNICA (default) del sub-almacén.");

        // Artículo debe existir y estar activo (read-port cross-módulo).
        var articulo = await _articulos.ObtenerAsync(request.ArticuloId, cancellationToken);
        if (articulo is null)
            throw new EntityNotFoundException(
                "ARTICULO_NO_ENCONTRADO",
                $"No existe artículo con id '{request.ArticuloId}'.");
        if (!articulo.EsActivo)
            throw new BusinessRuleException(
                "ARTICULO_INACTIVO",
                $"El artículo '{articulo.Clave}' está inactivo; no se puede asignar.");

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Unicidad por (ubicacion, articulo) — el índice único no incluye estatus,
        // así que a lo sumo hay una fila: activa → conflicto; inactiva → reactiva.
        var existente = await _db.AsignacionesArticuloUbicacion
            .FirstOrDefaultAsync(
                a => a.UbicacionId == request.UbicacionId && a.ArticuloId == request.ArticuloId,
                cancellationToken);

        AsignacionArticuloUbicacion asignacion;
        if (existente is not null)
        {
            if (existente.Estatus == EstatusCatalogo.Activo)
                throw new ConflictException(
                    "ASIGNACION_DUPLICADA",
                    $"El artículo '{articulo.Clave}' ya está asignado a esta ubicación.");

            existente.CambiarEstatus(EstatusCatalogo.Activo);
            asignacion = existente;
        }
        else
        {
            asignacion = new AsignacionArticuloUbicacion(
                id: Guid.CreateVersion7(),
                ubicacionId: request.UbicacionId,
                articuloId: request.ArticuloId);
            _db.AsignacionesArticuloUbicacion.Add(asignacion);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Fila de saldo en 0, idempotente. Si un movimiento ya la creó, DO NOTHING
        // (mismo conflict target que el trigger: (ubicacion_id, articulo_id)).
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.saldos_inventario
                (ubicacion_id, sub_almacen_id, articulo_id, cantidad,
                 costo_promedio_mxn, ultima_actualizacion_at)
            VALUES ({request.UbicacionId}, {ubicacion.SubAlmacenId}, {request.ArticuloId},
                    0, 0, NOW())
            ON CONFLICT (ubicacion_id, articulo_id) DO NOTHING",
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return ToResponse(asignacion);
    }

    internal static AsignacionResponse ToResponse(AsignacionArticuloUbicacion a) =>
        new(a.Id, a.UbicacionId, a.ArticuloId, a.Estatus);
}

// ─── Desasignar (guardrail EN_USO + borra fila-en-0) ──────────────────────────

public sealed record DesasignarArticuloDeUbicacionCommand(Guid Id) : IRequest<AsignacionResponse>;

public sealed class DesasignarArticuloDeUbicacionHandler
    : IRequestHandler<DesasignarArticuloDeUbicacionCommand, AsignacionResponse>
{
    private readonly AlmacenDbContext _db;

    public DesasignarArticuloDeUbicacionHandler(AlmacenDbContext db) => _db = db;

    public async Task<AsignacionResponse> Handle(
        DesasignarArticuloDeUbicacionCommand request, CancellationToken cancellationToken)
    {
        var asignacion = await _db.AsignacionesArticuloUbicacion
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ASIGNACION_NO_ENCONTRADA",
                $"No existe asignación con id '{request.Id}'.");

        // Idempotente: si ya está inactiva, no hay nada que hacer.
        if (asignacion.Estatus == EstatusCatalogo.Inactivo)
            return AsignarArticuloAUbicacionHandler.ToResponse(asignacion);

        // Guardrail "en uso": no se puede desasignar una ubicación con saldo > 0.
        var conSaldo = await _db.SaldosInventario.AsNoTracking()
            .AnyAsync(
                s => s.UbicacionId == asignacion.UbicacionId
                  && s.ArticuloId == asignacion.ArticuloId
                  && s.Cantidad > 0,
                cancellationToken);
        if (conSaldo)
            throw new BusinessRuleException(
                "ASIGNACION_EN_USO_CON_SALDO",
                "No se puede desasignar: la ubicación tiene saldo del artículo. Agote la existencia primero.");

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        asignacion.CambiarEstatus(EstatusCatalogo.Inactivo);
        await _db.SaveChangesAsync(cancellationToken);

        // Borra la fila-en-0 (garantizada en 0 por el guardrail; si tuviera stock
        // no habríamos llegado aquí). Si no existe, DELETE no afecta filas.
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM almacen.saldos_inventario
             WHERE ubicacion_id = {asignacion.UbicacionId}
               AND articulo_id = {asignacion.ArticuloId}",
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return AsignarArticuloAUbicacionHandler.ToResponse(asignacion);
    }
}
