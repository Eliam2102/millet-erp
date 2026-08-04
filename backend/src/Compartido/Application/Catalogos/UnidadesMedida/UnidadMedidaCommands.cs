using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.UnidadesMedida;

public sealed record UnidadMedidaResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    DimensionUnidad Dimension,
    decimal FactorABase,
    int Decimales,
    bool EsBase,
    EstatusCatalogo Estatus,
    int Version);

// ===== CREAR =====
public sealed record CrearUnidadMedidaCommand(
    string Codigo,
    string Nombre,
    DimensionUnidad Dimension,
    decimal FactorABase,
    int Decimales,
    bool EsBase) : IRequest<UnidadMedidaResponse>;

public sealed class CrearUnidadMedidaValidator : AbstractValidator<CrearUnidadMedidaCommand>
{
    public CrearUnidadMedidaValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Dimension).IsInEnum();
        RuleFor(c => c.FactorABase).GreaterThan(0m);
        RuleFor(c => c.Decimales).InclusiveBetween(0, 6);
    }
}

public sealed class CrearUnidadMedidaHandler
    : IRequestHandler<CrearUnidadMedidaCommand, UnidadMedidaResponse>
{
    private readonly CompartidoDbContext _db;
    public CrearUnidadMedidaHandler(CompartidoDbContext db) => _db = db;

    public async Task<UnidadMedidaResponse> Handle(
        CrearUnidadMedidaCommand command, CancellationToken cancellationToken)
    {
        var existe = await _db.UnidadesMedida.AsNoTracking()
            .AnyAsync(u => u.Codigo == command.Codigo, cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "UNIDAD_MEDIDA_CODIGO_DUPLICADO",
                $"Ya existe una unidad de medida con código '{command.Codigo}'.");
        }

        var entity = new UnidadMedida(
            Guid.CreateVersion7(), command.Codigo, command.Nombre, command.Dimension,
            command.FactorABase, command.Decimales, command.EsBase);
        _db.UnidadesMedida.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return UnidadMedidaMapper.ToResponse(entity);
    }
}

// ===== ACTUALIZAR =====
public sealed record ActualizarUnidadMedidaCommand(
    Guid Id,
    string? Nombre,
    int? Decimales,
    DimensionUnidad? Dimension,
    decimal? FactorABase,
    bool? EsBase) : IRequest<UnidadMedidaResponse>;

public sealed class ActualizarUnidadMedidaValidator : AbstractValidator<ActualizarUnidadMedidaCommand>
{
    public ActualizarUnidadMedidaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(100)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.Decimales!.Value).InclusiveBetween(0, 6)
            .When(c => c.Decimales.HasValue);
        RuleFor(c => c.Dimension!.Value).IsInEnum()
            .When(c => c.Dimension.HasValue);
        RuleFor(c => c.FactorABase!.Value).GreaterThan(0m)
            .When(c => c.FactorABase.HasValue);
    }
}

public sealed class ActualizarUnidadMedidaHandler
    : IRequestHandler<ActualizarUnidadMedidaCommand, UnidadMedidaResponse>
{
    private readonly CompartidoDbContext _db;
    public ActualizarUnidadMedidaHandler(CompartidoDbContext db) => _db = db;

    public async Task<UnidadMedidaResponse> Handle(
        ActualizarUnidadMedidaCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.UnidadesMedida
            .FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "UNIDAD_MEDIDA_NO_ENCONTRADA",
                $"No existe unidad de medida con id '{command.Id}'.");

        // Reconfiguración de conversión (dimensión/factor/base): solo si llega
        // alguno de los tres. El dominio bloquea con UNIDAD_MEDIDA_EN_USO si la
        // unidad está en uso (ADR-0046).
        if (command.Dimension.HasValue || command.FactorABase.HasValue || command.EsBase.HasValue)
        {
            // ADR-0046 Etapa 1b: "en uso" = existe algún artículo que referencia
            // esta unidad por FK (articulos.unidad_medida_id). Las líneas
            // snapshotean el string, no el FK, así que no cuentan. Mismo
            // DbContext (Articulos es DbSet de Compartido) → query inline, sin port.
            var estaEnUso = await _db.Articulos
                .AnyAsync(a => a.UnidadMedidaId == command.Id, cancellationToken);
            entity.ReconfigurarConversion(
                command.Dimension ?? entity.Dimension,
                command.FactorABase ?? entity.FactorABase,
                command.EsBase ?? entity.EsBase,
                estaEnUso);
        }

        entity.ActualizarDatos(
            nombre: command.Nombre,
            decimales: command.Decimales);
        await _db.SaveChangesAsync(cancellationToken);

        return UnidadMedidaMapper.ToResponse(entity);
    }
}

// ===== DESACTIVAR =====
public sealed record DesactivarUnidadMedidaCommand(Guid Id) : IRequest<UnidadMedidaResponse>;

public sealed class DesactivarUnidadMedidaHandler
    : IRequestHandler<DesactivarUnidadMedidaCommand, UnidadMedidaResponse>
{
    private readonly CompartidoDbContext _db;
    public DesactivarUnidadMedidaHandler(CompartidoDbContext db) => _db = db;

    public async Task<UnidadMedidaResponse> Handle(
        DesactivarUnidadMedidaCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.UnidadesMedida
            .FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "UNIDAD_MEDIDA_NO_ENCONTRADA",
                $"No existe unidad de medida con id '{command.Id}'.");

        if (entity.Estatus != EstatusCatalogo.Inactivo)
        {
            entity.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return UnidadMedidaMapper.ToResponse(entity);
    }
}

internal static class UnidadMedidaMapper
{
    public static UnidadMedidaResponse ToResponse(UnidadMedida e) => new(
        e.Id, e.Codigo, e.Nombre, e.Dimension, e.FactorABase,
        e.Decimales, e.EsBase, e.Estatus, e.Version);
}
