using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Departamentos;

/// <summary>
/// PATCH parcial sobre Departamento (F-Admin-PR2.3). Inmutable: Clave.
/// </summary>
public sealed record ActualizarDepartamentoCommand(
    Guid Id,
    string? Nombre) : IRequest<DepartamentoResponse>;

public sealed class ActualizarDepartamentoValidator : AbstractValidator<ActualizarDepartamentoCommand>
{
    public ActualizarDepartamentoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(254)
            .When(c => c.Nombre is not null);
    }
}

public sealed class ActualizarDepartamentoHandler
    : IRequestHandler<ActualizarDepartamentoCommand, DepartamentoResponse>
{
    private readonly CompartidoDbContext _db;

    public ActualizarDepartamentoHandler(CompartidoDbContext db) => _db = db;

    public async Task<DepartamentoResponse> Handle(
        ActualizarDepartamentoCommand command, CancellationToken cancellationToken)
    {
        var depto = await _db.Departamentos
            .FirstOrDefaultAsync(d => d.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "DEPARTAMENTO_NO_ENCONTRADO",
                $"No existe departamento con id '{command.Id}'.");

        depto.ActualizarDatos(nombre: command.Nombre);
        await _db.SaveChangesAsync(cancellationToken);

        return new DepartamentoResponse(
            depto.Id, depto.Clave, depto.Nombre, depto.Estatus, depto.Version);
    }
}
