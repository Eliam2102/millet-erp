using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.CondicionesPagoCatalogo;

public sealed record CondicionesPagoResponse(
    Guid Id,
    string Clave,
    string Nombre,
    int DiasCredito,
    EstatusCatalogo Estatus,
    int Version);

// ===== CREAR =====
public sealed record CrearCondicionesPagoCommand(
    string Clave,
    string Nombre,
    int DiasCredito) : IRequest<CondicionesPagoResponse>;

public sealed class CrearCondicionesPagoValidator : AbstractValidator<CrearCondicionesPagoCommand>
{
    public CrearCondicionesPagoValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(100);
        RuleFor(c => c.DiasCredito).InclusiveBetween(0, 365);
    }
}

public sealed class CrearCondicionesPagoHandler
    : IRequestHandler<CrearCondicionesPagoCommand, CondicionesPagoResponse>
{
    private readonly CompartidoDbContext _db;
    public CrearCondicionesPagoHandler(CompartidoDbContext db) => _db = db;

    public async Task<CondicionesPagoResponse> Handle(
        CrearCondicionesPagoCommand command, CancellationToken cancellationToken)
    {
        var existe = await _db.CondicionesPago.AsNoTracking()
            .AnyAsync(c => c.Clave == command.Clave, cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "CONDICIONES_PAGO_CLAVE_DUPLICADA",
                $"Ya existe condiciones de pago con clave '{command.Clave}'.");
        }

        var entity = new CondicionesPago(
            Guid.CreateVersion7(), command.Clave, command.Nombre, command.DiasCredito);
        _db.CondicionesPago.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new CondicionesPagoResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.DiasCredito,
            entity.Estatus, entity.Version);
    }
}

// ===== ACTUALIZAR =====
public sealed record ActualizarCondicionesPagoCommand(
    Guid Id,
    string? Nombre,
    int? DiasCredito) : IRequest<CondicionesPagoResponse>;

public sealed class ActualizarCondicionesPagoValidator : AbstractValidator<ActualizarCondicionesPagoCommand>
{
    public ActualizarCondicionesPagoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(100)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.DiasCredito!.Value).InclusiveBetween(0, 365)
            .When(c => c.DiasCredito.HasValue);
    }
}

public sealed class ActualizarCondicionesPagoHandler
    : IRequestHandler<ActualizarCondicionesPagoCommand, CondicionesPagoResponse>
{
    private readonly CompartidoDbContext _db;
    public ActualizarCondicionesPagoHandler(CompartidoDbContext db) => _db = db;

    public async Task<CondicionesPagoResponse> Handle(
        ActualizarCondicionesPagoCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.CondicionesPago
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CONDICIONES_PAGO_NO_ENCONTRADA",
                $"No existe condiciones de pago con id '{command.Id}'.");

        entity.ActualizarDatos(nombre: command.Nombre, diasCredito: command.DiasCredito);
        await _db.SaveChangesAsync(cancellationToken);

        return new CondicionesPagoResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.DiasCredito,
            entity.Estatus, entity.Version);
    }
}

// ===== DESACTIVAR =====
public sealed record DesactivarCondicionesPagoCommand(Guid Id) : IRequest<CondicionesPagoResponse>;

public sealed class DesactivarCondicionesPagoHandler
    : IRequestHandler<DesactivarCondicionesPagoCommand, CondicionesPagoResponse>
{
    private readonly CompartidoDbContext _db;
    public DesactivarCondicionesPagoHandler(CompartidoDbContext db) => _db = db;

    public async Task<CondicionesPagoResponse> Handle(
        DesactivarCondicionesPagoCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.CondicionesPago
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CONDICIONES_PAGO_NO_ENCONTRADA",
                $"No existe condiciones de pago con id '{command.Id}'.");

        if (entity.Estatus != EstatusCatalogo.Inactivo)
        {
            entity.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new CondicionesPagoResponse(
            entity.Id, entity.Clave, entity.Nombre, entity.DiasCredito,
            entity.Estatus, entity.Version);
    }
}
