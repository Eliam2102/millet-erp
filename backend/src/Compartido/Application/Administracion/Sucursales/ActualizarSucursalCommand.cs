using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Sucursales;

/// <summary>
/// PATCH parcial sobre Sucursal (F-Admin-PR2.3). Inmutable: Clave.
/// <see cref="LimpiarClaveAw"/> desasocia la sucursal de A+W (la clave
/// A+W sí es editable: corrige typos o re-asignaciones de la ingesta).
/// </summary>
public sealed record ActualizarSucursalCommand(
    Guid Id,
    string? Nombre,
    string? ClaveAw = null,
    bool LimpiarClaveAw = false,
    string? ZonaHoraria = null) : IRequest<SucursalResponse>;

public sealed class ActualizarSucursalValidator : AbstractValidator<ActualizarSucursalCommand>
{
    public ActualizarSucursalValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(254)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.ClaveAw!).NotEmpty().MaximumLength(40)
            .When(c => c.ClaveAw is not null);
        RuleFor(c => c.ClaveAw).Null()
            .When(c => c.LimpiarClaveAw)
            .WithMessage("No se puede enviar ClaveAw y LimpiarClaveAw a la vez.");
        // El id IANA se valida a fondo en el dominio (TimeZoneInfo).
        RuleFor(c => c.ZonaHoraria!).NotEmpty().MaximumLength(64)
            .When(c => c.ZonaHoraria is not null);
    }
}

public sealed class ActualizarSucursalHandler
    : IRequestHandler<ActualizarSucursalCommand, SucursalResponse>
{
    private readonly CompartidoDbContext _db;

    public ActualizarSucursalHandler(CompartidoDbContext db) => _db = db;

    public async Task<SucursalResponse> Handle(
        ActualizarSucursalCommand command, CancellationToken cancellationToken)
    {
        var sucursal = await _db.Sucursales
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{command.Id}'.");

        if (command.ClaveAw is not null)
        {
            var claveAwEnUso = await _db.Sucursales.AsNoTracking()
                .AnyAsync(s => s.Id != command.Id && s.ClaveAw == command.ClaveAw.Trim(),
                    cancellationToken);
            if (claveAwEnUso)
            {
                throw new ConflictException(
                    "SUCURSAL_CLAVE_AW_DUPLICADA",
                    $"Ya existe una sucursal con clave A+W '{command.ClaveAw.Trim()}'.");
            }
        }

        sucursal.ActualizarDatos(
            nombre: command.Nombre,
            claveAw: command.ClaveAw,
            limpiarClaveAw: command.LimpiarClaveAw,
            zonaHoraria: command.ZonaHoraria);
        await _db.SaveChangesAsync(cancellationToken);

        return new SucursalResponse(
            sucursal.Id, sucursal.Clave, sucursal.Nombre,
            sucursal.Estatus, sucursal.Version, sucursal.ClaveAw, sucursal.ZonaHoraria);
    }
}
