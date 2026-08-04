using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Commands de Dim2 (Nivel 2, CECO-PR4). Clave ÚNICA GLOBAL → 409. Padre
// (Dim1Id) INMUTABLE: Editar solo clave/nombre/grupo; reubicar = baja +
// alta. Guardrail CECO_PADRE_INACTIVO en Crear y Reactivar (sin él la
// cascada del ADR-0049 sería burlable).
// ============================================================================

public sealed record Dim2Response(
    Guid Id,
    Guid Dim1Id,
    string Clave,
    string Nombre,
    Guid GrupoDim2Id,
    EstatusCatalogo Estatus,
    int Version);

// ─── Crear ───────────────────────────────────────────────────────────────────

public sealed record CrearDim2Command(
    Guid Dim1Id,
    string Clave,
    string Nombre,
    Guid GrupoDim2Id) : IRequest<Dim2Response>;

public sealed class CrearDim2Validator : AbstractValidator<CrearDim2Command>
{
    public CrearDim2Validator()
    {
        RuleFor(c => c.Dim1Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.GrupoDim2Id).NotEqual(Guid.Empty);
    }
}

public sealed class CrearDim2Handler : IRequestHandler<CrearDim2Command, Dim2Response>
{
    private readonly CentrosCostoDbContext _db;

    public CrearDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim2Response> Handle(CrearDim2Command request, CancellationToken cancellationToken)
    {
        var dim1 = await _db.Dim1s.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Dim1Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM1_NO_ENCONTRADA",
                $"No existe Dimensión 1 con id '{request.Dim1Id}'.");

        if (dim1.Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException(
                "CECO_PADRE_INACTIVO",
                $"La Dimensión 1 '{dim1.Clave}' está inactiva; no admite dimensiones 2 nuevas.");

        var grupoActivo = await _db.GruposDim2.AsNoTracking()
            .AnyAsync(g => g.Id == request.GrupoDim2Id && g.Estatus == EstatusCatalogo.Activo, cancellationToken);
        if (!grupoActivo)
            throw new BusinessRuleException(
                "CECO_GRUPO_DIM2_INVALIDO",
                $"El grupo de dimensión 2 '{request.GrupoDim2Id}' no existe o está inactivo.");

        var clave = request.Clave.Trim();
        var claveExiste = await _db.Dim2s.AsNoTracking()
            .AnyAsync(d => d.Clave == clave, cancellationToken);
        if (claveExiste)
            throw new ConflictException(
                "CECO_CLAVE_DUPLICADA",
                $"Ya existe una Dimensión 2 con la clave '{clave}' (unicidad global del catálogo).");

        var dim2 = new Dim2(
            Guid.CreateVersion7(), request.Dim1Id, clave, request.Nombre.Trim(), request.GrupoDim2Id);
        _db.Dim2s.Add(dim2);
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim2Response(
            dim2.Id, dim2.Dim1Id, dim2.Clave, dim2.Nombre, dim2.GrupoDim2Id, dim2.Estatus, dim2.Version);
    }
}

// ─── Editar (clave/nombre/grupo — NUNCA el padre) ────────────────────────────

public sealed record EditarDim2Command(
    Guid Id,
    int VersionEsperada,
    string Clave,
    string Nombre,
    Guid GrupoDim2Id) : IRequest<Dim2Response>;

public sealed class EditarDim2Validator : AbstractValidator<EditarDim2Command>
{
    public EditarDim2Validator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.GrupoDim2Id).NotEqual(Guid.Empty);
    }
}

public sealed class EditarDim2Handler : IRequestHandler<EditarDim2Command, Dim2Response>
{
    private readonly CentrosCostoDbContext _db;

    public EditarDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim2Response> Handle(EditarDim2Command request, CancellationToken cancellationToken)
    {
        var dim2 = await _db.Dim2s
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM2_NO_ENCONTRADA", $"No existe Dimensión 2 con id '{request.Id}'.");

        if (dim2.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(Dim2), request.Id);

        if (dim2.GrupoDim2Id != request.GrupoDim2Id)
        {
            var grupoActivo = await _db.GruposDim2.AsNoTracking()
                .AnyAsync(g => g.Id == request.GrupoDim2Id && g.Estatus == EstatusCatalogo.Activo, cancellationToken);
            if (!grupoActivo)
                throw new BusinessRuleException(
                    "CECO_GRUPO_DIM2_INVALIDO",
                    $"El grupo de dimensión 2 '{request.GrupoDim2Id}' no existe o está inactivo.");
        }

        var clave = request.Clave.Trim();
        if (dim2.Clave != clave)
        {
            var claveExiste = await _db.Dim2s.AsNoTracking()
                .AnyAsync(d => d.Clave == clave && d.Id != request.Id, cancellationToken);
            if (claveExiste)
                throw new ConflictException(
                    "CECO_CLAVE_DUPLICADA",
                    $"Ya existe una Dimensión 2 con la clave '{clave}' (unicidad global del catálogo).");
        }

        dim2.Editar(clave, request.Nombre.Trim(), request.GrupoDim2Id);
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim2Response(
            dim2.Id, dim2.Dim1Id, dim2.Clave, dim2.Nombre, dim2.GrupoDim2Id, dim2.Estatus, dim2.Version);
    }
}

// ─── Desactivar (CASCADA: Dim3, un SaveChanges — ADR-0049) ───────────────────

public sealed record DesactivarDim2Response(
    Guid Id, EstatusCatalogo Estatus, int Dim3Desactivadas);

public sealed record DesactivarDim2Command(Guid Id, int VersionEsperada)
    : IRequest<DesactivarDim2Response>;

public sealed class DesactivarDim2Handler
    : IRequestHandler<DesactivarDim2Command, DesactivarDim2Response>
{
    private readonly CentrosCostoDbContext _db;

    public DesactivarDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<DesactivarDim2Response> Handle(
        DesactivarDim2Command request, CancellationToken cancellationToken)
    {
        var dim2 = await _db.Dim2s
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM2_NO_ENCONTRADA", $"No existe Dimensión 2 con id '{request.Id}'.");

        if (dim2.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(Dim2), request.Id);

        if (dim2.Estatus == EstatusCatalogo.Inactivo)
            return new DesactivarDim2Response(dim2.Id, dim2.Estatus, 0);

        var dim3s = await _db.Dim3s
            .Where(e => e.Dim2Id == dim2.Id && e.Estatus != EstatusCatalogo.Inactivo)
            .ToListAsync(cancellationToken);

        dim2.CambiarEstatus(EstatusCatalogo.Inactivo);
        foreach (var dim3 in dim3s)
            dim3.CambiarEstatus(EstatusCatalogo.Inactivo);

        await _db.SaveChangesAsync(cancellationToken);

        return new DesactivarDim2Response(dim2.Id, dim2.Estatus, dim3s.Count);
    }
}

// ─── Reactivar (guardrail: el padre debe estar activo; NO reactiva hijos) ────

public sealed record ReactivarDim2Command(Guid Id, int VersionEsperada) : IRequest<Dim2Response>;

public sealed class ReactivarDim2Handler : IRequestHandler<ReactivarDim2Command, Dim2Response>
{
    private readonly CentrosCostoDbContext _db;

    public ReactivarDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim2Response> Handle(ReactivarDim2Command request, CancellationToken cancellationToken)
    {
        var dim2 = await _db.Dim2s
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM2_NO_ENCONTRADA", $"No existe Dimensión 2 con id '{request.Id}'.");

        if (dim2.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(Dim2), request.Id);

        if (dim2.Estatus == EstatusCatalogo.Activo)
            return new Dim2Response(
                dim2.Id, dim2.Dim1Id, dim2.Clave, dim2.Nombre, dim2.GrupoDim2Id, dim2.Estatus, dim2.Version);

        var padreActivo = await _db.Dim1s.AsNoTracking()
            .AnyAsync(d => d.Id == dim2.Dim1Id && d.Estatus == EstatusCatalogo.Activo, cancellationToken);
        if (!padreActivo)
            throw new BusinessRuleException(
                "CECO_PADRE_INACTIVO",
                "La Dimensión 1 de esta Dimensión 2 está inactiva; reactívela primero.");

        dim2.CambiarEstatus(EstatusCatalogo.Activo);
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim2Response(
            dim2.Id, dim2.Dim1Id, dim2.Clave, dim2.Nombre, dim2.GrupoDim2Id, dim2.Estatus, dim2.Version);
    }
}
