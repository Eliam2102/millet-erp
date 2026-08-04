using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Departamentos;

/// <summary>
/// Crea un departamento (F-Admin-PR2.3). UNIQUE(clave) → 409
/// <c>DEPARTAMENTO_CLAVE_DUPLICADA</c>.
/// </summary>
public sealed record CrearDepartamentoCommand(
    Guid Id,
    string Clave,
    string Nombre) : IRequest<DepartamentoResponse>;

public sealed class CrearDepartamentoValidator : AbstractValidator<CrearDepartamentoCommand>
{
    public CrearDepartamentoValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
    }
}

public sealed class CrearDepartamentoHandler
    : IRequestHandler<CrearDepartamentoCommand, DepartamentoResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearDepartamentoHandler(CompartidoDbContext db) => _db = db;

    public async Task<DepartamentoResponse> Handle(
        CrearDepartamentoCommand command, CancellationToken cancellationToken)
    {
        var claveExiste = await _db.Departamentos.AsNoTracking()
            .AnyAsync(d => d.Clave == command.Clave, cancellationToken);
        if (claveExiste)
        {
            throw new ConflictException(
                "DEPARTAMENTO_CLAVE_DUPLICADA",
                $"Ya existe un departamento con clave '{command.Clave}'.");
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        var depto = new Departamento(id, command.Clave, command.Nombre);

        _db.Departamentos.Add(depto);
        await _db.SaveChangesAsync(cancellationToken);

        return new DepartamentoResponse(
            depto.Id, depto.Clave, depto.Nombre, depto.Estatus, depto.Version);
    }
}
