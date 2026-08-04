using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.CanalesVenta;

/// <summary>
/// Crea un canal de venta (FAC-ING-PR2). UNIQUE(nombre) → 409
/// <c>CANAL_VENTA_NOMBRE_DUPLICADO</c>; UNIQUE(clave_aw) → 409
/// <c>CANAL_VENTA_CLAVE_AW_DUPLICADA</c>.
///
/// <para>
/// El <c>Id</c> lo asigna el handler (max + 1) dentro de la transacción del
/// <c>SaveChanges</c> — la PK NO es identity (el seed 1..10 espejo del enum
/// original chocaría con la secuencia). Una carrera entre dos altas
/// concurrentes la resuelve la PK a nivel BD (23505 → 409 genérico).
/// </para>
/// </summary>
public sealed record CrearCanalVentaCommand(
    string Nombre,
    string? ClaveAw = null) : IRequest<CanalVentaResponse>;

public sealed class CrearCanalVentaValidator : AbstractValidator<CrearCanalVentaCommand>
{
    public CrearCanalVentaValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.ClaveAw!).NotEmpty().MaximumLength(40)
            .When(c => c.ClaveAw is not null);
    }
}

public sealed class CrearCanalVentaHandler
    : IRequestHandler<CrearCanalVentaCommand, CanalVentaResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearCanalVentaHandler(CompartidoDbContext db) => _db = db;

    public async Task<CanalVentaResponse> Handle(
        CrearCanalVentaCommand command, CancellationToken cancellationToken)
    {
        var nombreExiste = await _db.CanalesVenta.AsNoTracking()
            .AnyAsync(c => c.Nombre == command.Nombre, cancellationToken);
        if (nombreExiste)
        {
            throw new ConflictException(
                "CANAL_VENTA_NOMBRE_DUPLICADO",
                $"Ya existe un canal de venta con nombre '{command.Nombre}'.");
        }

        if (command.ClaveAw is not null)
        {
            var claveAwExiste = await _db.CanalesVenta.AsNoTracking()
                .AnyAsync(c => c.ClaveAw == command.ClaveAw.Trim(), cancellationToken);
            if (claveAwExiste)
            {
                throw new ConflictException(
                    "CANAL_VENTA_CLAVE_AW_DUPLICADA",
                    $"Ya existe un canal de venta con clave A+W '{command.ClaveAw.Trim()}'.");
            }
        }

        // Id = max + 1 asignado por la app (PK no-identity, ver doc del command).
        var maxId = await _db.CanalesVenta.AsNoTracking()
            .MaxAsync(c => (short?)c.Id, cancellationToken) ?? 0;
        var canal = new CanalVenta((short)(maxId + 1), command.Nombre, command.ClaveAw);

        _db.CanalesVenta.Add(canal);
        await _db.SaveChangesAsync(cancellationToken);

        return new CanalVentaResponse(
            canal.Id, canal.Nombre, canal.Estatus, canal.Version, canal.ClaveAw);
    }
}
