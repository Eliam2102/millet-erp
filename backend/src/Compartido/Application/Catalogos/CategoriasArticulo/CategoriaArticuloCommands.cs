using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.CategoriasArticulo;

public sealed record CategoriaArticuloResponse(
    Guid Id,
    string Nombre,
    EstatusCatalogo Estatus,
    int Version);

/// <summary>
/// Normalización del nombre para almacenamiento y unicidad: trim + colapso de
/// espacios internos, <b>preservando</b> mayúsculas y acentos del display. Es
/// el mismo criterio del índice de expresión UNIQUE de la migración
/// (<c>lower(regexp_replace(btrim(nombre),'\s+',' ','g'))</c>) y de la
/// reconciliación de PR3.
/// </summary>
internal static class CategoriaArticuloNormalizer
{
    public static string Normalizar(string nombre) =>
        Regex.Replace(nombre.Trim(), @"\s+", " ");

    public static CategoriaArticuloResponse ToResponse(CategoriaArticulo e) =>
        new(e.Id, e.Nombre, e.Estatus, e.Version);
}

// ===== CREAR =====
public sealed record CrearCategoriaArticuloCommand(string Nombre)
    : IRequest<CategoriaArticuloResponse>;

public sealed class CrearCategoriaArticuloValidator : AbstractValidator<CrearCategoriaArticuloCommand>
{
    public CrearCategoriaArticuloValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
    }
}

public sealed class CrearCategoriaArticuloHandler
    : IRequestHandler<CrearCategoriaArticuloCommand, CategoriaArticuloResponse>
{
    private readonly CompartidoDbContext _db;
    public CrearCategoriaArticuloHandler(CompartidoDbContext db) => _db = db;

    public async Task<CategoriaArticuloResponse> Handle(
        CrearCategoriaArticuloCommand command, CancellationToken cancellationToken)
    {
        var nombre = CategoriaArticuloNormalizer.Normalizar(command.Nombre);
        var norm = nombre.ToLowerInvariant();

        // Unicidad case-insensitive sobre el nombre normalizado. `Nombre` ya se
        // almacena normalizado, así que `lower(nombre)` equivale a la forma del
        // índice de expresión. `ToLower()` lo traduce EF a lower() de Postgres.
#pragma warning disable CA1304, CA1311, CA1862
        var existe = await _db.CategoriasArticulo.AsNoTracking()
            .AnyAsync(c => c.Nombre.ToLower() == norm, cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
        if (existe)
        {
            throw new ConflictException(
                "CATEGORIA_ARTICULO_NOMBRE_DUPLICADO",
                $"Ya existe una categoría de artículo con nombre '{nombre}'.");
        }

        var entity = new CategoriaArticulo(Guid.CreateVersion7(), nombre);
        _db.CategoriasArticulo.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return CategoriaArticuloNormalizer.ToResponse(entity);
    }
}

// ===== ACTUALIZAR =====
public sealed record ActualizarCategoriaArticuloCommand(Guid Id, string? Nombre)
    : IRequest<CategoriaArticuloResponse>;

public sealed class ActualizarCategoriaArticuloValidator
    : AbstractValidator<ActualizarCategoriaArticuloCommand>
{
    public ActualizarCategoriaArticuloValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(100)
            .When(c => c.Nombre is not null);
    }
}

public sealed class ActualizarCategoriaArticuloHandler
    : IRequestHandler<ActualizarCategoriaArticuloCommand, CategoriaArticuloResponse>
{
    private readonly CompartidoDbContext _db;
    public ActualizarCategoriaArticuloHandler(CompartidoDbContext db) => _db = db;

    public async Task<CategoriaArticuloResponse> Handle(
        ActualizarCategoriaArticuloCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.CategoriasArticulo
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CATEGORIA_ARTICULO_NO_ENCONTRADA",
                $"No existe categoría de artículo con id '{command.Id}'.");

        if (command.Nombre is not null)
        {
            var nombre = CategoriaArticuloNormalizer.Normalizar(command.Nombre);
            var norm = nombre.ToLowerInvariant();

#pragma warning disable CA1304, CA1311, CA1862
            var choca = await _db.CategoriasArticulo.AsNoTracking()
                .AnyAsync(c => c.Id != command.Id && c.Nombre.ToLower() == norm, cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
            if (choca)
            {
                throw new ConflictException(
                    "CATEGORIA_ARTICULO_NOMBRE_DUPLICADO",
                    $"Ya existe una categoría de artículo con nombre '{nombre}'.");
            }

            entity.ActualizarDatos(nombre: nombre);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return CategoriaArticuloNormalizer.ToResponse(entity);
    }
}

// ===== DESACTIVAR =====
public sealed record DesactivarCategoriaArticuloCommand(Guid Id)
    : IRequest<CategoriaArticuloResponse>;

public sealed class DesactivarCategoriaArticuloHandler
    : IRequestHandler<DesactivarCategoriaArticuloCommand, CategoriaArticuloResponse>
{
    private readonly CompartidoDbContext _db;
    public DesactivarCategoriaArticuloHandler(CompartidoDbContext db) => _db = db;

    public async Task<CategoriaArticuloResponse> Handle(
        DesactivarCategoriaArticuloCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.CategoriasArticulo
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CATEGORIA_ARTICULO_NO_ENCONTRADA",
                $"No existe categoría de artículo con id '{command.Id}'.");

        if (entity.Estatus != EstatusCatalogo.Inactivo)
        {
            // Guardrail "en uso" (ADR-0046, PR2 — ya existe articulos.categoria_id):
            // no se puede desactivar una categoría referenciada por algún artículo.
            // Gemelo a nivel app del ON DELETE RESTRICT de la FK (que solo cubre
            // el borrado físico; esto cubre el soft-delete).
            var enUso = await _db.Articulos
                .AnyAsync(a => a.CategoriaId == command.Id, cancellationToken);
            if (enUso)
            {
                throw new BusinessRuleException(
                    "CATEGORIA_ARTICULO_EN_USO",
                    "No se puede desactivar una categoría asignada a uno o más artículos.");
            }

            entity.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return CategoriaArticuloNormalizer.ToResponse(entity);
    }
}
