using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.UsosPrincipales;

public sealed record UsoPrincipalResponse(
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus,
    int Version);

// ===== CREAR =====
public sealed record CrearUsoPrincipalCommand(string Clave, string Nombre)
    : IRequest<UsoPrincipalResponse>;

public sealed class CrearUsoPrincipalValidator : AbstractValidator<CrearUsoPrincipalCommand>
{
    public CrearUsoPrincipalValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
    }
}

public sealed class CrearUsoPrincipalHandler
    : IRequestHandler<CrearUsoPrincipalCommand, UsoPrincipalResponse>
{
    private readonly CompartidoDbContext _db;
    public CrearUsoPrincipalHandler(CompartidoDbContext db) => _db = db;

    public async Task<UsoPrincipalResponse> Handle(
        CrearUsoPrincipalCommand command, CancellationToken cancellationToken)
    {
        var existe = await _db.UsosPrincipales.AsNoTracking()
            .AnyAsync(u => u.Clave == command.Clave, cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "USO_PRINCIPAL_CLAVE_DUPLICADA",
                $"Ya existe uso principal con clave '{command.Clave}'.");
        }

        var entity = new UsoPrincipal(Guid.CreateVersion7(), command.Clave, command.Nombre);
        _db.UsosPrincipales.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new UsoPrincipalResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.Estatus, entity.Version);
    }
}

// ===== ACTUALIZAR =====
public sealed record ActualizarUsoPrincipalCommand(
    Guid Id,
    string? Nombre) : IRequest<UsoPrincipalResponse>;

public sealed class ActualizarUsoPrincipalValidator : AbstractValidator<ActualizarUsoPrincipalCommand>
{
    public ActualizarUsoPrincipalValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(100)
            .When(c => c.Nombre is not null);
    }
}

public sealed class ActualizarUsoPrincipalHandler
    : IRequestHandler<ActualizarUsoPrincipalCommand, UsoPrincipalResponse>
{
    private readonly CompartidoDbContext _db;
    public ActualizarUsoPrincipalHandler(CompartidoDbContext db) => _db = db;

    public async Task<UsoPrincipalResponse> Handle(
        ActualizarUsoPrincipalCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.UsosPrincipales
            .FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USO_PRINCIPAL_NO_ENCONTRADO",
                $"No existe uso principal con id '{command.Id}'.");

        entity.ActualizarDatos(nombre: command.Nombre);
        await _db.SaveChangesAsync(cancellationToken);

        return new UsoPrincipalResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.Estatus, entity.Version);
    }
}

// ===== DESACTIVAR =====
public sealed record DesactivarUsoPrincipalCommand(Guid Id) : IRequest<UsoPrincipalResponse>;

public sealed class DesactivarUsoPrincipalHandler
    : IRequestHandler<DesactivarUsoPrincipalCommand, UsoPrincipalResponse>
{
    private readonly CompartidoDbContext _db;
    public DesactivarUsoPrincipalHandler(CompartidoDbContext db) => _db = db;

    public async Task<UsoPrincipalResponse> Handle(
        DesactivarUsoPrincipalCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.UsosPrincipales
            .FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USO_PRINCIPAL_NO_ENCONTRADO",
                $"No existe uso principal con id '{command.Id}'.");

        if (entity.Estatus != EstatusCatalogo.Inactivo)
        {
            entity.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new UsoPrincipalResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.Estatus, entity.Version);
    }
}
