using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.CanalesVenta;

/// <summary>
/// PATCH parcial sobre CanalVenta (FAC-ING-PR2). Inmutable: <c>Id</c> (ya
/// persistido en pedidos y facturas). Editables: nombre, clave A+W
/// (<see cref="LimpiarClaveAw"/> desasocia el canal de la ingesta) y
/// estatus (Activo/Inactivo/EnRevision — desactivar saca el canal de los
/// selectores y de la resolución de ingesta sin tocar el histórico).
/// </summary>
public sealed record ActualizarCanalVentaCommand(
    short Id,
    string? Nombre,
    string? ClaveAw = null,
    bool LimpiarClaveAw = false,
    EstatusCatalogo? Estatus = null) : IRequest<CanalVentaResponse>;

public sealed class ActualizarCanalVentaValidator : AbstractValidator<ActualizarCanalVentaCommand>
{
    public ActualizarCanalVentaValidator()
    {
        RuleFor(c => c.Id).GreaterThan((short)0);
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(254)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.ClaveAw!).NotEmpty().MaximumLength(40)
            .When(c => c.ClaveAw is not null);
        RuleFor(c => c.ClaveAw).Null()
            .When(c => c.LimpiarClaveAw)
            .WithMessage("No se puede enviar ClaveAw y LimpiarClaveAw a la vez.");
        RuleFor(c => c.Estatus).IsInEnum()
            .When(c => c.Estatus is not null);
    }
}

public sealed class ActualizarCanalVentaHandler
    : IRequestHandler<ActualizarCanalVentaCommand, CanalVentaResponse>
{
    private readonly CompartidoDbContext _db;

    public ActualizarCanalVentaHandler(CompartidoDbContext db) => _db = db;

    public async Task<CanalVentaResponse> Handle(
        ActualizarCanalVentaCommand command, CancellationToken cancellationToken)
    {
        var canal = await _db.CanalesVenta
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CANAL_VENTA_NO_ENCONTRADO",
                $"No existe canal de venta con id '{command.Id}'.");

        if (command.Nombre is not null)
        {
            var nombreEnUso = await _db.CanalesVenta.AsNoTracking()
                .AnyAsync(c => c.Id != command.Id && c.Nombre == command.Nombre,
                    cancellationToken);
            if (nombreEnUso)
            {
                throw new ConflictException(
                    "CANAL_VENTA_NOMBRE_DUPLICADO",
                    $"Ya existe un canal de venta con nombre '{command.Nombre}'.");
            }
        }

        if (command.ClaveAw is not null)
        {
            var claveAwEnUso = await _db.CanalesVenta.AsNoTracking()
                .AnyAsync(c => c.Id != command.Id && c.ClaveAw == command.ClaveAw.Trim(),
                    cancellationToken);
            if (claveAwEnUso)
            {
                throw new ConflictException(
                    "CANAL_VENTA_CLAVE_AW_DUPLICADA",
                    $"Ya existe un canal de venta con clave A+W '{command.ClaveAw.Trim()}'.");
            }
        }

        canal.ActualizarDatos(
            nombre: command.Nombre,
            claveAw: command.ClaveAw,
            limpiarClaveAw: command.LimpiarClaveAw);
        if (command.Estatus is EstatusCatalogo estatus)
            canal.CambiarEstatus(estatus);

        await _db.SaveChangesAsync(cancellationToken);

        return new CanalVentaResponse(
            canal.Id, canal.Nombre, canal.Estatus, canal.Version, canal.ClaveAw);
    }
}
