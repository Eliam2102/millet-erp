using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Puestos;

/// <summary>
/// PATCH parcial sobre Puesto (ADM-PR1). Inmutable: Clave.
/// </summary>
public sealed record ActualizarPuestoCommand(
    Guid Id,
    string? Nombre) : IRequest<PuestoResponse>;

public sealed class ActualizarPuestoValidator : AbstractValidator<ActualizarPuestoCommand>
{
    public ActualizarPuestoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(254)
            .When(c => c.Nombre is not null);
    }
}

public sealed class ActualizarPuestoHandler
    : IRequestHandler<ActualizarPuestoCommand, PuestoResponse>
{
    private readonly CompartidoDbContext _db;

    public ActualizarPuestoHandler(CompartidoDbContext db) => _db = db;

    public async Task<PuestoResponse> Handle(
        ActualizarPuestoCommand command, CancellationToken cancellationToken)
    {
        var puesto = await _db.Puestos
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PUESTO_NO_ENCONTRADO",
                $"No existe puesto con id '{command.Id}'.");

        puesto.ActualizarDatos(nombre: command.Nombre);
        await _db.SaveChangesAsync(cancellationToken);

        return new PuestoResponse(
            puesto.Id, puesto.Clave, puesto.Nombre, puesto.Estatus, puesto.Version);
    }
}
