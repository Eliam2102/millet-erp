using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.Transportistas;

public sealed record TransportistaResponse(
    Guid Id,
    string Clave,
    string Nombre,
    string? Email,
    string? Telefono,
    EstatusCatalogo Estatus,
    int Version);

// ===== CREAR =====
public sealed record CrearTransportistaCommand(
    string Clave,
    string Nombre,
    string? Email,
    string? Telefono) : IRequest<TransportistaResponse>;

public sealed class CrearTransportistaValidator : AbstractValidator<CrearTransportistaCommand>
{
    public CrearTransportistaValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Email!).EmailAddress().MaximumLength(254)
            .When(c => !string.IsNullOrEmpty(c.Email));
        RuleFor(c => c.Telefono!).MaximumLength(50).When(c => c.Telefono is not null);
    }
}

public sealed class CrearTransportistaHandler
    : IRequestHandler<CrearTransportistaCommand, TransportistaResponse>
{
    private readonly CompartidoDbContext _db;
    public CrearTransportistaHandler(CompartidoDbContext db) => _db = db;

    public async Task<TransportistaResponse> Handle(
        CrearTransportistaCommand command, CancellationToken cancellationToken)
    {
        var existe = await _db.Transportistas.AsNoTracking()
            .AnyAsync(t => t.Clave == command.Clave, cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "TRANSPORTISTA_CLAVE_DUPLICADA",
                $"Ya existe transportista con clave '{command.Clave}'.");
        }

        var entity = new Transportista(
            Guid.CreateVersion7(), command.Clave, command.Nombre, command.Email, command.Telefono);
        _db.Transportistas.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new TransportistaResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.Email, entity.Telefono,
            entity.Estatus, entity.Version);
    }
}

// ===== ACTUALIZAR =====
public sealed record ActualizarTransportistaCommand(
    Guid Id,
    string? Nombre,
    string? Email,
    string? Telefono,
    bool LimpiarEmail,
    bool LimpiarTelefono) : IRequest<TransportistaResponse>;

public sealed class ActualizarTransportistaValidator : AbstractValidator<ActualizarTransportistaCommand>
{
    public ActualizarTransportistaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(254)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.Email!).EmailAddress().MaximumLength(254)
            .When(c => !string.IsNullOrEmpty(c.Email));
        RuleFor(c => c.Telefono!).MaximumLength(50).When(c => c.Telefono is not null);
    }
}

public sealed class ActualizarTransportistaHandler
    : IRequestHandler<ActualizarTransportistaCommand, TransportistaResponse>
{
    private readonly CompartidoDbContext _db;
    public ActualizarTransportistaHandler(CompartidoDbContext db) => _db = db;

    public async Task<TransportistaResponse> Handle(
        ActualizarTransportistaCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.Transportistas
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "TRANSPORTISTA_NO_ENCONTRADO",
                $"No existe transportista con id '{command.Id}'.");

        entity.ActualizarDatos(
            nombre: command.Nombre,
            email: command.Email,
            telefono: command.Telefono,
            limpiarEmail: command.LimpiarEmail,
            limpiarTelefono: command.LimpiarTelefono);
        await _db.SaveChangesAsync(cancellationToken);

        return new TransportistaResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.Email, entity.Telefono,
            entity.Estatus, entity.Version);
    }
}

// ===== DESACTIVAR =====
public sealed record DesactivarTransportistaCommand(Guid Id) : IRequest<TransportistaResponse>;

public sealed class DesactivarTransportistaHandler
    : IRequestHandler<DesactivarTransportistaCommand, TransportistaResponse>
{
    private readonly CompartidoDbContext _db;
    public DesactivarTransportistaHandler(CompartidoDbContext db) => _db = db;

    public async Task<TransportistaResponse> Handle(
        DesactivarTransportistaCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.Transportistas
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "TRANSPORTISTA_NO_ENCONTRADO",
                $"No existe transportista con id '{command.Id}'.");

        if (entity.Estatus != EstatusCatalogo.Inactivo)
        {
            entity.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new TransportistaResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.Email, entity.Telefono,
            entity.Estatus, entity.Version);
    }
}
