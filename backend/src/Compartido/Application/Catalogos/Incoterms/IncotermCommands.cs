using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.Incoterms;

public sealed record IncotermResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    EstatusCatalogo Estatus,
    int Version);

// ===== CREAR =====
public sealed record CrearIncotermCommand(string Codigo, string Nombre) : IRequest<IncotermResponse>;

public sealed class CrearIncotermValidator : AbstractValidator<CrearIncotermCommand>
{
    public CrearIncotermValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().Length(2, 4);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
    }
}

public sealed class CrearIncotermHandler
    : IRequestHandler<CrearIncotermCommand, IncotermResponse>
{
    private readonly CompartidoDbContext _db;
    public CrearIncotermHandler(CompartidoDbContext db) => _db = db;

    public async Task<IncotermResponse> Handle(
        CrearIncotermCommand command, CancellationToken cancellationToken)
    {
        var codigo = command.Codigo.ToUpperInvariant();
        var existe = await _db.Incoterms.AsNoTracking()
            .AnyAsync(i => i.Codigo == codigo, cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "INCOTERM_CODIGO_DUPLICADO",
                $"Ya existe Incoterm con código '{codigo}'.");
        }

        var entity = new Incoterm(Guid.CreateVersion7(), codigo, command.Nombre);
        _db.Incoterms.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new IncotermResponse(entity.Id, entity.Codigo, entity.Nombre, entity.Estatus, entity.Version);
    }
}

// ===== ACTUALIZAR =====
public sealed record ActualizarIncotermCommand(
    Guid Id,
    string? Nombre) : IRequest<IncotermResponse>;

public sealed class ActualizarIncotermValidator : AbstractValidator<ActualizarIncotermCommand>
{
    public ActualizarIncotermValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(100)
            .When(c => c.Nombre is not null);
    }
}

public sealed class ActualizarIncotermHandler
    : IRequestHandler<ActualizarIncotermCommand, IncotermResponse>
{
    private readonly CompartidoDbContext _db;
    public ActualizarIncotermHandler(CompartidoDbContext db) => _db = db;

    public async Task<IncotermResponse> Handle(
        ActualizarIncotermCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.Incoterms
            .FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "INCOTERM_NO_ENCONTRADO",
                $"No existe Incoterm con id '{command.Id}'.");

        entity.ActualizarDatos(nombre: command.Nombre);
        await _db.SaveChangesAsync(cancellationToken);

        return new IncotermResponse(entity.Id, entity.Codigo, entity.Nombre, entity.Estatus, entity.Version);
    }
}

// ===== DESACTIVAR =====
public sealed record DesactivarIncotermCommand(Guid Id) : IRequest<IncotermResponse>;

public sealed class DesactivarIncotermHandler
    : IRequestHandler<DesactivarIncotermCommand, IncotermResponse>
{
    private readonly CompartidoDbContext _db;
    public DesactivarIncotermHandler(CompartidoDbContext db) => _db = db;

    public async Task<IncotermResponse> Handle(
        DesactivarIncotermCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.Incoterms
            .FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "INCOTERM_NO_ENCONTRADO",
                $"No existe Incoterm con id '{command.Id}'.");

        if (entity.Estatus != EstatusCatalogo.Inactivo)
        {
            entity.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new IncotermResponse(entity.Id, entity.Codigo, entity.Nombre, entity.Estatus, entity.Version);
    }
}
