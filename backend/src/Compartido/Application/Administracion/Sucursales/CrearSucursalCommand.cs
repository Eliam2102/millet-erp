using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Events;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Sucursales;

/// <summary>
/// Crea una sucursal (F-Admin-PR2.3). UNIQUE(clave) → 409
/// <c>SUCURSAL_CLAVE_DUPLICADA</c>.
///
/// <para>
/// <see cref="Id"/> = <see cref="Guid.Empty"/> ⇒ se autogenera con
/// <c>Guid.CreateVersion7()</c>. Publica
/// <see cref="SucursalCreadaEvent"/>.
/// </para>
/// </summary>
public sealed record CrearSucursalCommand(
    Guid Id,
    string Clave,
    string Nombre,
    string? ClaveAw = null) : IRequest<SucursalResponse>;

public sealed class CrearSucursalValidator : AbstractValidator<CrearSucursalCommand>
{
    public CrearSucursalValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.ClaveAw!).NotEmpty().MaximumLength(40)
            .When(c => c.ClaveAw is not null);
    }
}

public sealed class CrearSucursalHandler
    : IRequestHandler<CrearSucursalCommand, SucursalResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly IClock _clock;

    public CrearSucursalHandler(
        CompartidoDbContext db,
        IIntegrationEventPublisher events,
        IClock clock)
    {
        _db = db;
        _events = events;
        _clock = clock;
    }

    public async Task<SucursalResponse> Handle(
        CrearSucursalCommand command, CancellationToken cancellationToken)
    {
        var claveExiste = await _db.Sucursales.AsNoTracking()
            .AnyAsync(s => s.Clave == command.Clave, cancellationToken);
        if (claveExiste)
        {
            throw new ConflictException(
                "SUCURSAL_CLAVE_DUPLICADA",
                $"Ya existe una sucursal con clave '{command.Clave}'.");
        }

        if (command.ClaveAw is not null)
        {
            var claveAwExiste = await _db.Sucursales.AsNoTracking()
                .AnyAsync(s => s.ClaveAw == command.ClaveAw.Trim(), cancellationToken);
            if (claveAwExiste)
            {
                throw new ConflictException(
                    "SUCURSAL_CLAVE_AW_DUPLICADA",
                    $"Ya existe una sucursal con clave A+W '{command.ClaveAw.Trim()}'.");
            }
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        var sucursal = new Sucursal(id, command.Clave, command.Nombre, claveAw: command.ClaveAw);

        _db.Sucursales.Add(sucursal);
        await _db.SaveChangesAsync(cancellationToken);

        // PLATFORM-TODO(<AdminOutbox>): ver nota en CrearEmpresaHandler.
        await _events.PublishAsync(
            new SucursalCreadaEvent(
                sucursal.Id, sucursal.Clave, sucursal.Nombre, _clock.UtcNow),
            cancellationToken);

        return new SucursalResponse(
            sucursal.Id, sucursal.Clave, sucursal.Nombre,
            sucursal.Estatus, sucursal.Version, sucursal.ClaveAw, sucursal.ZonaHoraria);
    }
}
