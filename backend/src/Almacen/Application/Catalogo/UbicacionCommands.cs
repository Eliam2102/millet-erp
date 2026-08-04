using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Catalogo;

// ============================================================================
// Commands del catálogo de Ubicaciones (Nivel 4, ADR-0047, PR1).
//
// La ubicación se gestiona standalone (DbSet propio + handler directo),
// igual que SubAlmacén respecto de Almacén: el handler valida que el
// sub-almacén padre exista por id y aplica la unicidad compuesta
// `(sub_almacen_id, clave)` (respaldada por el índice único
// `ux_ubicaciones_sub_almacen_clave`).
// ============================================================================

// ─── Crear Ubicación ─────────────────────────────────────────────────────────

public sealed record CrearUbicacionCommand(
    Guid SubAlmacenId,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus) : IRequest<CrearUbicacionResponse>;

public sealed record CrearUbicacionResponse(Guid Id, Guid SubAlmacenId, string Clave);

public sealed class CrearUbicacionValidator : AbstractValidator<CrearUbicacionCommand>
{
    public CrearUbicacionValidator()
    {
        RuleFor(c => c.SubAlmacenId).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Estatus).IsInEnum();
    }
}

public sealed class CrearUbicacionHandler
    : IRequestHandler<CrearUbicacionCommand, CrearUbicacionResponse>
{
    private readonly AlmacenDbContext _db;

    public CrearUbicacionHandler(AlmacenDbContext db) => _db = db;

    public async Task<CrearUbicacionResponse> Handle(
        CrearUbicacionCommand request, CancellationToken cancellationToken)
    {
        var subAlmacenExiste = await _db.SubAlmacenes.AsNoTracking()
            .AnyAsync(s => s.Id == request.SubAlmacenId, cancellationToken);
        if (!subAlmacenExiste)
        {
            throw new EntityNotFoundException(
                "SUBALMACEN_NO_ENCONTRADO",
                $"No existe sub-almacén con id '{request.SubAlmacenId}'.");
        }

        var claveExiste = await _db.Ubicaciones.AsNoTracking()
            .AnyAsync(u => u.SubAlmacenId == request.SubAlmacenId && u.Clave == request.Clave,
                cancellationToken);
        if (claveExiste)
        {
            throw new BusinessRuleException(
                "UBICACION_CLAVE_DUPLICADA",
                $"Ya existe una ubicación con clave '{request.Clave}' en el sub-almacén.");
        }

        var ubicacion = new Ubicacion(
            id: Guid.CreateVersion7(),
            subAlmacenId: request.SubAlmacenId,
            clave: request.Clave,
            nombre: request.Nombre,
            estatus: request.Estatus);

        _db.Ubicaciones.Add(ubicacion);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearUbicacionResponse(ubicacion.Id, ubicacion.SubAlmacenId, ubicacion.Clave);
    }
}

// ─── Editar Ubicación ────────────────────────────────────────────────────────
//
// Acotado a clave/nombre (ADR-0047 PR C7.1): el estatus NO se toca aquí. La
// baja/reactivación va por los endpoints dedicados desactivar/reactivar, que
// aplican el guardrail de saldo y el blindaje de la ubicación default (ÚNICA).
// Así el PATCH no puede saltarse esas validaciones.

public sealed record EditarUbicacionCommand(
    Guid Id,
    string Clave,
    string Nombre) : IRequest;

public sealed class EditarUbicacionValidator : AbstractValidator<EditarUbicacionCommand>
{
    public EditarUbicacionValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
    }
}

public sealed class EditarUbicacionHandler : IRequestHandler<EditarUbicacionCommand>
{
    private readonly AlmacenDbContext _db;

    public EditarUbicacionHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(EditarUbicacionCommand request, CancellationToken cancellationToken)
    {
        var ubicacion = await _db.Ubicaciones
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "UBICACION_NO_ENCONTRADA",
                $"No existe ubicación con id '{request.Id}'.");

        if (ubicacion.Clave != request.Clave)
        {
            var claveExiste = await _db.Ubicaciones.AsNoTracking()
                .AnyAsync(u => u.SubAlmacenId == ubicacion.SubAlmacenId
                    && u.Clave == request.Clave
                    && u.Id != request.Id,
                    cancellationToken);
            if (claveExiste)
            {
                throw new BusinessRuleException(
                    "UBICACION_CLAVE_DUPLICADA",
                    $"Ya existe una ubicación con clave '{request.Clave}' en el sub-almacén.");
            }
        }

        ubicacion.Editar(request.Clave, request.Nombre);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Respuesta de cambio de estatus (desactivar/reactivar) ────────────────────

public sealed record UbicacionEstatusResponse(Guid Id, EstatusCatalogo Estatus);

// ─── Desactivar Ubicación (doble guardrail: default + saldo) ──────────────────

public sealed record DesactivarUbicacionCommand(Guid Id) : IRequest<UbicacionEstatusResponse>;

public sealed class DesactivarUbicacionHandler
    : IRequestHandler<DesactivarUbicacionCommand, UbicacionEstatusResponse>
{
    private readonly AlmacenDbContext _db;

    public DesactivarUbicacionHandler(AlmacenDbContext db) => _db = db;

    public async Task<UbicacionEstatusResponse> Handle(
        DesactivarUbicacionCommand request, CancellationToken cancellationToken)
    {
        var ubicacion = await _db.Ubicaciones
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "UBICACION_NO_ENCONTRADA",
                $"No existe ubicación con id '{request.Id}'.");

        // Idempotente: si ya está inactiva, no hay nada que hacer.
        if (ubicacion.Estatus == EstatusCatalogo.Inactivo)
            return new UbicacionEstatusResponse(ubicacion.Id, ubicacion.Estatus);

        // Guardrail 1: la ubicación default (ÚNICA) es el destino del trigger de
        // saldos; desactivarla rompería el enrutamiento (UBICACION_DEFAULT_INEXISTENTE
        // al registrar un movimiento). No es desactivable hasta que C7.2 mueva el
        // enrutamiento a la ubicación capturada en el movimiento.
        if (ubicacion.EsDefault)
            throw new BusinessRuleException(
                "UBICACION_DEFAULT_NO_DESACTIVABLE",
                "No se puede desactivar la ubicación default del sub-almacén: es el destino de enrutamiento de saldos.");

        // Guardrail 2: no se puede desactivar una ubicación con saldo > 0 de
        // cualquier artículo (molde del guardrail de desasignar, pero por ubicación).
        var conSaldo = await _db.SaldosInventario.AsNoTracking()
            .AnyAsync(s => s.UbicacionId == request.Id && s.Cantidad > 0, cancellationToken);
        if (conSaldo)
            throw new BusinessRuleException(
                "UBICACION_EN_USO_CON_SALDO",
                "No se puede desactivar: la ubicación tiene existencia de al menos un artículo. Agote el saldo primero.");

        ubicacion.CambiarEstatus(EstatusCatalogo.Inactivo);
        await _db.SaveChangesAsync(cancellationToken);

        return new UbicacionEstatusResponse(ubicacion.Id, ubicacion.Estatus);
    }
}

// ─── Reactivar Ubicación (sin guardrail) ──────────────────────────────────────

public sealed record ReactivarUbicacionCommand(Guid Id) : IRequest<UbicacionEstatusResponse>;

public sealed class ReactivarUbicacionHandler
    : IRequestHandler<ReactivarUbicacionCommand, UbicacionEstatusResponse>
{
    private readonly AlmacenDbContext _db;

    public ReactivarUbicacionHandler(AlmacenDbContext db) => _db = db;

    public async Task<UbicacionEstatusResponse> Handle(
        ReactivarUbicacionCommand request, CancellationToken cancellationToken)
    {
        var ubicacion = await _db.Ubicaciones
            .FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "UBICACION_NO_ENCONTRADA",
                $"No existe ubicación con id '{request.Id}'.");

        // Idempotente + sin guardrail: reactivar una ubicación es inocuo. El índice
        // único (sub_almacen_id, clave) —que ignora el estatus— garantiza que su
        // clave no fue tomada por otra mientras estuvo inactiva.
        if (ubicacion.Estatus == EstatusCatalogo.Activo)
            return new UbicacionEstatusResponse(ubicacion.Id, ubicacion.Estatus);

        ubicacion.CambiarEstatus(EstatusCatalogo.Activo);
        await _db.SaveChangesAsync(cancellationToken);

        return new UbicacionEstatusResponse(ubicacion.Id, ubicacion.Estatus);
    }
}
