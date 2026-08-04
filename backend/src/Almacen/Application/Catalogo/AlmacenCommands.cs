using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Catalogo;

// ============================================================================
// Commands del catálogo Almacén / SubAlmacén (F1-PR1).
//
// La unicidad de `Clave` es global en `almacenes` (UNIQUE) y compuesta
// `(almacen_id, clave)` en `sub_almacenes`. Cross-module: la sucursal se
// valida vía ISucursalReadPort — pero en F1-PR1 ese puerto es NoOp, así
// que la validación sólo verifica que el GUID no esté vacío. Cuando entre
// el adapter real (PLATFORM-TODO <SucursalReadAdapter>), el handler
// rechazará sucursales inexistentes.
// ============================================================================

// ─── Crear Almacén ───────────────────────────────────────────────────────────

public sealed record CrearAlmacenCommand(
    string Clave,
    string Nombre,
    Guid SucursalId,
    EstatusCatalogo Estatus) : IRequest<CrearAlmacenResponse>;

public sealed record CrearAlmacenResponse(Guid Id, string Clave);

public sealed class CrearAlmacenValidator : AbstractValidator<CrearAlmacenCommand>
{
    public CrearAlmacenValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.SucursalId).NotEqual(Guid.Empty);
        RuleFor(c => c.Estatus).IsInEnum();
    }
}

public sealed class CrearAlmacenHandler
    : IRequestHandler<CrearAlmacenCommand, CrearAlmacenResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ISucursalReadPort _sucursales;

    public CrearAlmacenHandler(AlmacenDbContext db, ISucursalReadPort sucursales)
    {
        _db = db;
        _sucursales = sucursales;
    }

    public async Task<CrearAlmacenResponse> Handle(
        CrearAlmacenCommand request, CancellationToken cancellationToken)
    {
        // ISucursalReadPort NoOp en F0-PR1 devuelve null para cualquier id;
        // el handler trata null como "no validable" (acepta) hasta que el
        // adapter real entre. Si el adapter responde, sí valida.
        var sucursal = await _sucursales.ObtenerAsync(request.SucursalId, cancellationToken);
        if (sucursal is not null && !sucursal.EsActiva)
        {
            throw new BusinessRuleException(
                "ALMACEN_SUCURSAL_INACTIVA",
                $"La sucursal '{request.SucursalId}' está inactiva.");
        }

        var claveExiste = await _db.Almacenes.AsNoTracking()
            .AnyAsync(a => a.Clave == request.Clave, cancellationToken);
        if (claveExiste)
        {
            throw new BusinessRuleException(
                "ALMACEN_CLAVE_DUPLICADA",
                $"Ya existe un almacén con clave '{request.Clave}'.");
        }

        var almacen = new Domain.Catalogo.Almacen(
            id: Guid.CreateVersion7(),
            clave: request.Clave,
            nombre: request.Nombre,
            sucursalId: request.SucursalId,
            estatus: request.Estatus);

        _db.Almacenes.Add(almacen);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearAlmacenResponse(almacen.Id, almacen.Clave);
    }
}

// ─── Editar Almacén ──────────────────────────────────────────────────────────

public sealed record EditarAlmacenCommand(
    Guid Id,
    string Clave,
    string Nombre,
    Guid SucursalId,
    EstatusCatalogo Estatus) : IRequest;

public sealed class EditarAlmacenValidator : AbstractValidator<EditarAlmacenCommand>
{
    public EditarAlmacenValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.SucursalId).NotEqual(Guid.Empty);
        RuleFor(c => c.Estatus).IsInEnum();
    }
}

public sealed class EditarAlmacenHandler : IRequestHandler<EditarAlmacenCommand>
{
    private readonly AlmacenDbContext _db;

    public EditarAlmacenHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(EditarAlmacenCommand request, CancellationToken cancellationToken)
    {
        var almacen = await _db.Almacenes
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ALMACEN_NO_ENCONTRADO",
                $"No existe almacén con id '{request.Id}'.");

        // Si la clave cambió, validar unicidad.
        if (almacen.Clave != request.Clave)
        {
            var claveExiste = await _db.Almacenes.AsNoTracking()
                .AnyAsync(a => a.Clave == request.Clave && a.Id != request.Id, cancellationToken);
            if (claveExiste)
            {
                throw new BusinessRuleException(
                    "ALMACEN_CLAVE_DUPLICADA",
                    $"Ya existe un almacén con clave '{request.Clave}'.");
            }
        }

        almacen.Editar(request.Clave, request.Nombre, request.SucursalId);
        almacen.CambiarEstatus(request.Estatus);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Crear SubAlmacén ────────────────────────────────────────────────────────

public sealed record CrearSubAlmacenCommand(
    Guid AlmacenId,
    string Clave,
    string Nombre,
    TipoSubAlmacen Tipo,
    EstatusCatalogo Estatus) : IRequest<CrearSubAlmacenResponse>;

public sealed record CrearSubAlmacenResponse(Guid Id, Guid AlmacenId, string Clave);

public sealed class CrearSubAlmacenValidator : AbstractValidator<CrearSubAlmacenCommand>
{
    public CrearSubAlmacenValidator()
    {
        RuleFor(c => c.AlmacenId).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Tipo).IsInEnum();
        RuleFor(c => c.Estatus).IsInEnum();
    }
}

public sealed class CrearSubAlmacenHandler
    : IRequestHandler<CrearSubAlmacenCommand, CrearSubAlmacenResponse>
{
    private readonly AlmacenDbContext _db;

    public CrearSubAlmacenHandler(AlmacenDbContext db) => _db = db;

    public async Task<CrearSubAlmacenResponse> Handle(
        CrearSubAlmacenCommand request, CancellationToken cancellationToken)
    {
        var almacenExiste = await _db.Almacenes.AsNoTracking()
            .AnyAsync(a => a.Id == request.AlmacenId, cancellationToken);
        if (!almacenExiste)
        {
            throw new EntityNotFoundException(
                "ALMACEN_NO_ENCONTRADO",
                $"No existe almacén con id '{request.AlmacenId}'.");
        }

        var claveExiste = await _db.SubAlmacenes.AsNoTracking()
            .AnyAsync(s => s.AlmacenId == request.AlmacenId && s.Clave == request.Clave,
                cancellationToken);
        if (claveExiste)
        {
            throw new BusinessRuleException(
                "SUBALMACEN_CLAVE_DUPLICADA",
                $"Ya existe sub-almacén con clave '{request.Clave}' en el almacén.");
        }

        var sub = new SubAlmacen(
            id: Guid.CreateVersion7(),
            almacenId: request.AlmacenId,
            clave: request.Clave,
            nombre: request.Nombre,
            tipo: request.Tipo,
            estatus: request.Estatus);

        _db.SubAlmacenes.Add(sub);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearSubAlmacenResponse(sub.Id, sub.AlmacenId, sub.Clave);
    }
}

// ─── Editar SubAlmacén ───────────────────────────────────────────────────────

public sealed record EditarSubAlmacenCommand(
    Guid Id,
    string Clave,
    string Nombre,
    TipoSubAlmacen Tipo,
    EstatusCatalogo Estatus) : IRequest;

public sealed class EditarSubAlmacenValidator : AbstractValidator<EditarSubAlmacenCommand>
{
    public EditarSubAlmacenValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Tipo).IsInEnum();
        RuleFor(c => c.Estatus).IsInEnum();
    }
}

public sealed class EditarSubAlmacenHandler : IRequestHandler<EditarSubAlmacenCommand>
{
    private readonly AlmacenDbContext _db;

    public EditarSubAlmacenHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(EditarSubAlmacenCommand request, CancellationToken cancellationToken)
    {
        var sub = await _db.SubAlmacenes
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUBALMACEN_NO_ENCONTRADO",
                $"No existe sub-almacén con id '{request.Id}'.");

        if (sub.Clave != request.Clave)
        {
            var claveExiste = await _db.SubAlmacenes.AsNoTracking()
                .AnyAsync(s => s.AlmacenId == sub.AlmacenId
                    && s.Clave == request.Clave
                    && s.Id != request.Id,
                    cancellationToken);
            if (claveExiste)
            {
                throw new BusinessRuleException(
                    "SUBALMACEN_CLAVE_DUPLICADA",
                    $"Ya existe sub-almacén con clave '{request.Clave}' en el almacén.");
            }
        }

        sub.Editar(request.Clave, request.Nombre, request.Tipo);
        sub.CambiarEstatus(request.Estatus);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
