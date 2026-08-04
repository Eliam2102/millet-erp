using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Commands de Dim3 (Nivel 3 — la hoja; "Máquina" en documentos, CECO-PR4).
// Clave ÚNICA GLOBAL → 409. Padre (Dim2Id) INMUTABLE. Sin cascada (es
// hoja); guardrail CECO_PADRE_INACTIVO en Crear y Reactivar.
// ============================================================================

public sealed record Dim3Response(
    Guid Id,
    Guid Dim2Id,
    string Clave,
    string Nombre,
    Guid GrupoDim3Id,
    EstatusCatalogo Estatus,
    int Version);

// ─── Crear ───────────────────────────────────────────────────────────────────

public sealed record CrearDim3Command(
    Guid Dim2Id,
    string Clave,
    string Nombre,
    Guid GrupoDim3Id) : IRequest<Dim3Response>;

public sealed class CrearDim3Validator : AbstractValidator<CrearDim3Command>
{
    public CrearDim3Validator()
    {
        RuleFor(c => c.Dim2Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.GrupoDim3Id).NotEqual(Guid.Empty);
    }
}

public sealed class CrearDim3Handler : IRequestHandler<CrearDim3Command, Dim3Response>
{
    private readonly CentrosCostoDbContext _db;

    public CrearDim3Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim3Response> Handle(CrearDim3Command request, CancellationToken cancellationToken)
    {
        var dim2 = await _db.Dim2s.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Dim2Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM2_NO_ENCONTRADA",
                $"No existe Dimensión 2 con id '{request.Dim2Id}'.");

        if (dim2.Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException(
                "CECO_PADRE_INACTIVO",
                $"La Dimensión 2 '{dim2.Clave}' está inactiva; no admite dimensiones 3 nuevas.");

        var grupoActivo = await _db.GruposDim3.AsNoTracking()
            .AnyAsync(g => g.Id == request.GrupoDim3Id && g.Estatus == EstatusCatalogo.Activo, cancellationToken);
        if (!grupoActivo)
            throw new BusinessRuleException(
                "CECO_GRUPO_DIM3_INVALIDO",
                $"El grupo de dimensión 3 '{request.GrupoDim3Id}' no existe o está inactivo.");

        var clave = request.Clave.Trim();
        var claveExiste = await _db.Dim3s.AsNoTracking()
            .AnyAsync(e => e.Clave == clave, cancellationToken);
        if (claveExiste)
            throw new ConflictException(
                "CECO_CLAVE_DUPLICADA",
                $"Ya existe una Dimensión 3 con la clave '{clave}' (unicidad global del catálogo).");

        var dim3 = new Dim3(
            Guid.CreateVersion7(), request.Dim2Id, clave, request.Nombre.Trim(), request.GrupoDim3Id);
        _db.Dim3s.Add(dim3);
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim3Response(
            dim3.Id, dim3.Dim2Id, dim3.Clave, dim3.Nombre, dim3.GrupoDim3Id, dim3.Estatus, dim3.Version);
    }
}

// ─── Editar (clave/nombre/grupo — NUNCA el padre) ────────────────────────────

public sealed record EditarDim3Command(
    Guid Id,
    int VersionEsperada,
    string Clave,
    string Nombre,
    Guid GrupoDim3Id) : IRequest<Dim3Response>;

public sealed class EditarDim3Validator : AbstractValidator<EditarDim3Command>
{
    public EditarDim3Validator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.GrupoDim3Id).NotEqual(Guid.Empty);
    }
}

public sealed class EditarDim3Handler : IRequestHandler<EditarDim3Command, Dim3Response>
{
    private readonly CentrosCostoDbContext _db;

    public EditarDim3Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim3Response> Handle(EditarDim3Command request, CancellationToken cancellationToken)
    {
        var dim3 = await _db.Dim3s
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM3_NO_ENCONTRADA", $"No existe Dimensión 3 con id '{request.Id}'.");

        if (dim3.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(Dim3), request.Id);

        if (dim3.GrupoDim3Id != request.GrupoDim3Id)
        {
            var grupoActivo = await _db.GruposDim3.AsNoTracking()
                .AnyAsync(g => g.Id == request.GrupoDim3Id && g.Estatus == EstatusCatalogo.Activo, cancellationToken);
            if (!grupoActivo)
                throw new BusinessRuleException(
                    "CECO_GRUPO_DIM3_INVALIDO",
                    $"El grupo de dimensión 3 '{request.GrupoDim3Id}' no existe o está inactivo.");
        }

        var clave = request.Clave.Trim();
        if (dim3.Clave != clave)
        {
            var claveExiste = await _db.Dim3s.AsNoTracking()
                .AnyAsync(e => e.Clave == clave && e.Id != request.Id, cancellationToken);
            if (claveExiste)
                throw new ConflictException(
                    "CECO_CLAVE_DUPLICADA",
                    $"Ya existe una Dimensión 3 con la clave '{clave}' (unicidad global del catálogo).");
        }

        dim3.Editar(clave, request.Nombre.Trim(), request.GrupoDim3Id);
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim3Response(
            dim3.Id, dim3.Dim2Id, dim3.Clave, dim3.Nombre, dim3.GrupoDim3Id, dim3.Estatus, dim3.Version);
    }
}

// ─── Desactivar / Reactivar (hoja: sin cascada; guardrail al reactivar) ──────

public sealed record CambiarEstatusDim3Command(Guid Id, int VersionEsperada, bool Activar)
    : IRequest<Dim3Response>;

public sealed class CambiarEstatusDim3Handler
    : IRequestHandler<CambiarEstatusDim3Command, Dim3Response>
{
    private readonly CentrosCostoDbContext _db;

    public CambiarEstatusDim3Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim3Response> Handle(
        CambiarEstatusDim3Command request, CancellationToken cancellationToken)
    {
        var dim3 = await _db.Dim3s
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM3_NO_ENCONTRADA", $"No existe Dimensión 3 con id '{request.Id}'.");

        if (dim3.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(Dim3), request.Id);

        var destino = request.Activar ? EstatusCatalogo.Activo : EstatusCatalogo.Inactivo;

        if (dim3.Estatus == destino)
            return new Dim3Response(
                dim3.Id, dim3.Dim2Id, dim3.Clave, dim3.Nombre, dim3.GrupoDim3Id, dim3.Estatus, dim3.Version);

        if (request.Activar)
        {
            var padreActivo = await _db.Dim2s.AsNoTracking()
                .AnyAsync(d => d.Id == dim3.Dim2Id && d.Estatus == EstatusCatalogo.Activo, cancellationToken);
            if (!padreActivo)
                throw new BusinessRuleException(
                    "CECO_PADRE_INACTIVO",
                    "La Dimensión 2 de esta Dimensión 3 está inactiva; reactívela primero.");
        }

        dim3.CambiarEstatus(destino);
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim3Response(
            dim3.Id, dim3.Dim2Id, dim3.Clave, dim3.Nombre, dim3.GrupoDim3Id, dim3.Estatus, dim3.Version);
    }
}
