using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Puestos;

/// <summary>
/// Crea un puesto (ADM-PR1). UNIQUE(clave) → 409
/// <c>PUESTO_CLAVE_DUPLICADA</c>.
/// </summary>
public sealed record CrearPuestoCommand(
    Guid Id,
    string Clave,
    string Nombre) : IRequest<PuestoResponse>;

public sealed class CrearPuestoValidator : AbstractValidator<CrearPuestoCommand>
{
    public CrearPuestoValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
    }
}

public sealed class CrearPuestoHandler
    : IRequestHandler<CrearPuestoCommand, PuestoResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearPuestoHandler(CompartidoDbContext db) => _db = db;

    public async Task<PuestoResponse> Handle(
        CrearPuestoCommand command, CancellationToken cancellationToken)
    {
        var claveExiste = await _db.Puestos.AsNoTracking()
            .AnyAsync(p => p.Clave == command.Clave, cancellationToken);
        if (claveExiste)
        {
            throw new ConflictException(
                "PUESTO_CLAVE_DUPLICADA",
                $"Ya existe un puesto con clave '{command.Clave}'.");
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        var puesto = new Puesto(id, command.Clave, command.Nombre);

        _db.Puestos.Add(puesto);
        await _db.SaveChangesAsync(cancellationToken);

        return new PuestoResponse(
            puesto.Id, puesto.Clave, puesto.Nombre, puesto.Estatus, puesto.Version);
    }
}
