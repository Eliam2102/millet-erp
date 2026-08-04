using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Commands de Dim1 (Nivel 1, CECO-PR4). Catálogo PROPIO — sin vínculo a
// compartido.sucursales (separación total, 01-diseno §1): crear/editar son
// CRUD normal con Clave (única global, ej. "101") + Nombre. Desactivar
// CASCADA sobre Dim2 y Dim3 en un solo SaveChanges (ADR-0049).
// ============================================================================

public sealed record Dim1Response(
    Guid Id, string Clave, string Nombre, EstatusCatalogo Estatus, int Version);

// ─── Crear ───────────────────────────────────────────────────────────────────

public sealed record CrearDim1Command(string Clave, string Nombre) : IRequest<Dim1Response>;

public sealed class CrearDim1Validator : AbstractValidator<CrearDim1Command>
{
    public CrearDim1Validator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(10);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
    }
}

public sealed class CrearDim1Handler : IRequestHandler<CrearDim1Command, Dim1Response>
{
    private readonly CentrosCostoDbContext _db;

    public CrearDim1Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim1Response> Handle(CrearDim1Command request, CancellationToken cancellationToken)
    {
        var clave = request.Clave.Trim();
        var claveExiste = await _db.Dim1s.AsNoTracking()
            .AnyAsync(d => d.Clave == clave, cancellationToken);
        if (claveExiste)
            throw new ConflictException(
                "CECO_CLAVE_DUPLICADA",
                $"Ya existe una Dimensión 1 con la clave '{clave}'.");

        var dim1 = new Dim1(Guid.CreateVersion7(), clave, request.Nombre.Trim());
        _db.Dim1s.Add(dim1);
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim1Response(dim1.Id, dim1.Clave, dim1.Nombre, dim1.Estatus, dim1.Version);
    }
}

// ─── Editar (clave/nombre) ───────────────────────────────────────────────────

public sealed record EditarDim1Command(Guid Id, int VersionEsperada, string Clave, string Nombre)
    : IRequest<Dim1Response>;

public sealed class EditarDim1Validator : AbstractValidator<EditarDim1Command>
{
    public EditarDim1Validator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(10);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
    }
}

public sealed class EditarDim1Handler : IRequestHandler<EditarDim1Command, Dim1Response>
{
    private readonly CentrosCostoDbContext _db;

    public EditarDim1Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim1Response> Handle(EditarDim1Command request, CancellationToken cancellationToken)
    {
        var dim1 = await _db.Dim1s
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM1_NO_ENCONTRADA", $"No existe Dimensión 1 con id '{request.Id}'.");

        if (dim1.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(Dim1), request.Id);

        var clave = request.Clave.Trim();
        if (dim1.Clave != clave)
        {
            var claveExiste = await _db.Dim1s.AsNoTracking()
                .AnyAsync(d => d.Clave == clave && d.Id != request.Id, cancellationToken);
            if (claveExiste)
                throw new ConflictException(
                    "CECO_CLAVE_DUPLICADA",
                    $"Ya existe una Dimensión 1 con la clave '{clave}'.");
        }

        dim1.Editar(clave, request.Nombre.Trim());
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim1Response(dim1.Id, dim1.Clave, dim1.Nombre, dim1.Estatus, dim1.Version);
    }
}

// ─── Desactivar (CASCADA: Dim2 + Dim3, un SaveChanges — ADR-0049) ────────────

public sealed record DesactivarDim1Response(
    Guid Id,
    EstatusCatalogo Estatus,
    int Dim2Desactivadas,
    int Dim3Desactivadas);

public sealed record DesactivarDim1Command(Guid Id, int VersionEsperada)
    : IRequest<DesactivarDim1Response>;

public sealed class DesactivarDim1Handler
    : IRequestHandler<DesactivarDim1Command, DesactivarDim1Response>
{
    private readonly CentrosCostoDbContext _db;

    public DesactivarDim1Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<DesactivarDim1Response> Handle(
        DesactivarDim1Command request, CancellationToken cancellationToken)
    {
        var dim1 = await _db.Dim1s
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM1_NO_ENCONTRADA", $"No existe Dimensión 1 con id '{request.Id}'.");

        if (dim1.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(Dim1), request.Id);

        // Idempotente: si ya está inactiva, el subárbol ya fue cascadeado.
        if (dim1.Estatus == EstatusCatalogo.Inactivo)
            return new DesactivarDim1Response(dim1.Id, dim1.Estatus, 0, 0);

        // Cascada explícita a dos niveles (todo lo no-Inactivo, EnRevision
        // incluido) en UN SOLO SaveChanges — transaccional por construcción
        // (ADR-0049). El If-Match protege al padre; los hijos quedan
        // cubiertos por su concurrency token.
        var dim2s = await _db.Dim2s
            .Where(d => d.Dim1Id == dim1.Id && d.Estatus != EstatusCatalogo.Inactivo)
            .ToListAsync(cancellationToken);

        var dim2Ids = dim2s.Select(d => d.Id).ToList();
        var dim3s = await _db.Dim3s
            .Where(e => dim2Ids.Contains(e.Dim2Id) && e.Estatus != EstatusCatalogo.Inactivo)
            .ToListAsync(cancellationToken);

        dim1.CambiarEstatus(EstatusCatalogo.Inactivo);
        foreach (var dim2 in dim2s)
            dim2.CambiarEstatus(EstatusCatalogo.Inactivo);
        foreach (var dim3 in dim3s)
            dim3.CambiarEstatus(EstatusCatalogo.Inactivo);

        await _db.SaveChangesAsync(cancellationToken);

        return new DesactivarDim1Response(dim1.Id, dim1.Estatus, dim2s.Count, dim3s.Count);
    }
}

// ─── Reactivar (NO reactiva hijos — decisión de diseño) ──────────────────────

public sealed record ReactivarDim1Command(Guid Id, int VersionEsperada) : IRequest<Dim1Response>;

public sealed class ReactivarDim1Handler : IRequestHandler<ReactivarDim1Command, Dim1Response>
{
    private readonly CentrosCostoDbContext _db;

    public ReactivarDim1Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim1Response> Handle(ReactivarDim1Command request, CancellationToken cancellationToken)
    {
        var dim1 = await _db.Dim1s
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM1_NO_ENCONTRADA", $"No existe Dimensión 1 con id '{request.Id}'.");

        if (dim1.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(Dim1), request.Id);

        if (dim1.Estatus == EstatusCatalogo.Activo)
            return new Dim1Response(dim1.Id, dim1.Clave, dim1.Nombre, dim1.Estatus, dim1.Version);

        // Reactivar es solo el nodo: los descendientes cascadeados se
        // reactivan uno a uno, con intención (ADR-0049).
        dim1.CambiarEstatus(EstatusCatalogo.Activo);
        await _db.SaveChangesAsync(cancellationToken);

        return new Dim1Response(dim1.Id, dim1.Clave, dim1.Nombre, dim1.Estatus, dim1.Version);
    }
}
