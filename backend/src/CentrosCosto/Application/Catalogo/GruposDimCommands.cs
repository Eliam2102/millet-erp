using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Commands de los grupos GrupoDim2 y GrupoDim3 (CECO-PR4).
//
// Catálogos GLOBALES que clasifican Dim2/Dim3 — NO son niveles (01-diseno
// §3). CRUD mínimo: crear, renombrar, desactivar, reactivar. Unicidad de
// nombre → ConflictException 409. Los grupos NO cascadan: desactivarlos
// solo los retira de la clasificación nueva.
// ============================================================================

public sealed record GrupoDimResponse(Guid Id, string Nombre, EstatusCatalogo Estatus, int Version);

// ─── GrupoDim2 ───────────────────────────────────────────────────────────────

public sealed record CrearGrupoDim2Command(string Nombre) : IRequest<GrupoDimResponse>;

public sealed class CrearGrupoDim2Validator : AbstractValidator<CrearGrupoDim2Command>
{
    public CrearGrupoDim2Validator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
    }
}

public sealed class CrearGrupoDim2Handler : IRequestHandler<CrearGrupoDim2Command, GrupoDimResponse>
{
    private readonly CentrosCostoDbContext _db;

    public CrearGrupoDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<GrupoDimResponse> Handle(CrearGrupoDim2Command request, CancellationToken cancellationToken)
    {
        var nombre = request.Nombre.Trim();
        var existe = await _db.GruposDim2.AsNoTracking()
            .AnyAsync(g => g.Nombre == nombre, cancellationToken);
        if (existe)
            throw new ConflictException(
                "CECO_GRUPO_DIM2_NOMBRE_DUPLICADO",
                $"Ya existe un grupo de dimensión 2 con el nombre '{nombre}'.");

        var grupo = new GrupoDim2(Guid.CreateVersion7(), nombre);
        _db.GruposDim2.Add(grupo);
        await _db.SaveChangesAsync(cancellationToken);

        return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);
    }
}

public sealed record RenombrarGrupoDim2Command(Guid Id, int VersionEsperada, string Nombre) : IRequest<GrupoDimResponse>;

public sealed class RenombrarGrupoDim2Validator : AbstractValidator<RenombrarGrupoDim2Command>
{
    public RenombrarGrupoDim2Validator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
    }
}

public sealed class RenombrarGrupoDim2Handler : IRequestHandler<RenombrarGrupoDim2Command, GrupoDimResponse>
{
    private readonly CentrosCostoDbContext _db;

    public RenombrarGrupoDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<GrupoDimResponse> Handle(RenombrarGrupoDim2Command request, CancellationToken cancellationToken)
    {
        var grupo = await _db.GruposDim2
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_GRUPO_DIM2_NO_ENCONTRADO", $"No existe grupo de dimensión 2 con id '{request.Id}'.");

        if (grupo.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(GrupoDim2), request.Id);

        var nombre = request.Nombre.Trim();
        if (grupo.Nombre != nombre)
        {
            var existe = await _db.GruposDim2.AsNoTracking()
                .AnyAsync(g => g.Nombre == nombre && g.Id != request.Id, cancellationToken);
            if (existe)
                throw new ConflictException(
                    "CECO_GRUPO_DIM2_NOMBRE_DUPLICADO",
                    $"Ya existe un grupo de dimensión 2 con el nombre '{nombre}'.");
        }

        grupo.Renombrar(nombre);
        await _db.SaveChangesAsync(cancellationToken);

        return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);
    }
}

public sealed record CambiarEstatusGrupoDim2Command(Guid Id, int VersionEsperada, bool Activar) : IRequest<GrupoDimResponse>;

public sealed class CambiarEstatusGrupoDim2Handler : IRequestHandler<CambiarEstatusGrupoDim2Command, GrupoDimResponse>
{
    private readonly CentrosCostoDbContext _db;

    public CambiarEstatusGrupoDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<GrupoDimResponse> Handle(CambiarEstatusGrupoDim2Command request, CancellationToken cancellationToken)
    {
        var grupo = await _db.GruposDim2
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_GRUPO_DIM2_NO_ENCONTRADO", $"No existe grupo de dimensión 2 con id '{request.Id}'.");

        if (grupo.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(GrupoDim2), request.Id);

        var destino = request.Activar ? EstatusCatalogo.Activo : EstatusCatalogo.Inactivo;

        if (grupo.Estatus == destino)
            return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);

        grupo.CambiarEstatus(destino);
        await _db.SaveChangesAsync(cancellationToken);

        return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);
    }
}

// ─── GrupoDim3 (misma forma) ─────────────────────────────────────────────────

public sealed record CrearGrupoDim3Command(string Nombre) : IRequest<GrupoDimResponse>;

public sealed class CrearGrupoDim3Validator : AbstractValidator<CrearGrupoDim3Command>
{
    public CrearGrupoDim3Validator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
    }
}

public sealed class CrearGrupoDim3Handler : IRequestHandler<CrearGrupoDim3Command, GrupoDimResponse>
{
    private readonly CentrosCostoDbContext _db;

    public CrearGrupoDim3Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<GrupoDimResponse> Handle(CrearGrupoDim3Command request, CancellationToken cancellationToken)
    {
        var nombre = request.Nombre.Trim();
        var existe = await _db.GruposDim3.AsNoTracking()
            .AnyAsync(g => g.Nombre == nombre, cancellationToken);
        if (existe)
            throw new ConflictException(
                "CECO_GRUPO_DIM3_NOMBRE_DUPLICADO",
                $"Ya existe un grupo de dimensión 3 con el nombre '{nombre}'.");

        var grupo = new GrupoDim3(Guid.CreateVersion7(), nombre);
        _db.GruposDim3.Add(grupo);
        await _db.SaveChangesAsync(cancellationToken);

        return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);
    }
}

public sealed record RenombrarGrupoDim3Command(Guid Id, int VersionEsperada, string Nombre) : IRequest<GrupoDimResponse>;

public sealed class RenombrarGrupoDim3Validator : AbstractValidator<RenombrarGrupoDim3Command>
{
    public RenombrarGrupoDim3Validator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
    }
}

public sealed class RenombrarGrupoDim3Handler : IRequestHandler<RenombrarGrupoDim3Command, GrupoDimResponse>
{
    private readonly CentrosCostoDbContext _db;

    public RenombrarGrupoDim3Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<GrupoDimResponse> Handle(RenombrarGrupoDim3Command request, CancellationToken cancellationToken)
    {
        var grupo = await _db.GruposDim3
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_GRUPO_DIM3_NO_ENCONTRADO", $"No existe grupo de dimensión 3 con id '{request.Id}'.");

        if (grupo.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(GrupoDim3), request.Id);

        var nombre = request.Nombre.Trim();
        if (grupo.Nombre != nombre)
        {
            var existe = await _db.GruposDim3.AsNoTracking()
                .AnyAsync(g => g.Nombre == nombre && g.Id != request.Id, cancellationToken);
            if (existe)
                throw new ConflictException(
                    "CECO_GRUPO_DIM3_NOMBRE_DUPLICADO",
                    $"Ya existe un grupo de dimensión 3 con el nombre '{nombre}'.");
        }

        grupo.Renombrar(nombre);
        await _db.SaveChangesAsync(cancellationToken);

        return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);
    }
}

public sealed record CambiarEstatusGrupoDim3Command(Guid Id, int VersionEsperada, bool Activar) : IRequest<GrupoDimResponse>;

public sealed class CambiarEstatusGrupoDim3Handler : IRequestHandler<CambiarEstatusGrupoDim3Command, GrupoDimResponse>
{
    private readonly CentrosCostoDbContext _db;

    public CambiarEstatusGrupoDim3Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<GrupoDimResponse> Handle(CambiarEstatusGrupoDim3Command request, CancellationToken cancellationToken)
    {
        var grupo = await _db.GruposDim3
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_GRUPO_DIM3_NO_ENCONTRADO", $"No existe grupo de dimensión 3 con id '{request.Id}'.");

        if (grupo.Version != request.VersionEsperada)
            throw new ConcurrencyException(nameof(GrupoDim3), request.Id);

        var destino = request.Activar ? EstatusCatalogo.Activo : EstatusCatalogo.Inactivo;

        if (grupo.Estatus == destino)
            return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);

        grupo.CambiarEstatus(destino);
        await _db.SaveChangesAsync(cancellationToken);

        return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);
    }
}
