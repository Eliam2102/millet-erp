using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Puestos;

/// <summary>
/// PATCH parcial sobre Puesto (ADM-PR1, F1-ADM-01.4). Inmutable: Clave.
/// Permite actualizar Nombre, asociar/desasociar RolSugeridoId y DepartamentoId.
/// </summary>
public sealed record ActualizarPuestoCommand(
    Guid Id,
    string? Nombre,
    Guid? RolSugeridoId = null,
    bool LimpiarRolSugerido = false,
    Guid? DepartamentoId = null,
    bool LimpiarDepartamento = false) : IRequest<PuestoResponse>;

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
    private readonly IRolReadPort _rolReadPort;

    public ActualizarPuestoHandler(
        CompartidoDbContext db,
        IRolReadPort rolReadPort)
    {
        _db = db;
        _rolReadPort = rolReadPort;
    }

    public async Task<PuestoResponse> Handle(
        ActualizarPuestoCommand command, CancellationToken cancellationToken)
    {
        var puesto = await _db.Puestos
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PUESTO_NO_ENCONTRADO",
                $"No existe puesto con id '{command.Id}'.");

        if (!command.LimpiarRolSugerido && command.RolSugeridoId.HasValue)
        {
            var rolActivo = await _rolReadPort.ExisteActivoAsync(
                command.RolSugeridoId.Value, cancellationToken);
            if (!rolActivo)
            {
                throw new ConflictException(
                    "ROL_SUGERIDO_INVALIDO",
                    $"El rol sugerido '{command.RolSugeridoId.Value}' no existe o está inactivo.");
            }
        }

        if (!command.LimpiarDepartamento && command.DepartamentoId.HasValue)
        {
            var deptoExiste = await _db.Departamentos.AsNoTracking()
                .AnyAsync(d => d.EmpresaId == puesto.EmpresaId && d.Id == command.DepartamentoId.Value, cancellationToken);
            if (!deptoExiste)
            {
                throw new ConflictException(
                    "DEPARTAMENTO_INVALIDO",
                    $"El departamento '{command.DepartamentoId.Value}' no existe en esta empresa.");
            }
        }

        puesto.ActualizarDatos(
            nombre: command.Nombre,
            rolSugeridoId: command.RolSugeridoId,
            limpiarRolSugerido: command.LimpiarRolSugerido,
            departamentoId: command.DepartamentoId,
            limpiarDepartamento: command.LimpiarDepartamento);

        await _db.SaveChangesAsync(cancellationToken);

        string? rolNombre = null;
        if (puesto.RolSugeridoId.HasValue)
        {
            var dict = await _rolReadPort.ObtenerNombresPorIdsAsync(
                new[] { puesto.RolSugeridoId.Value }, cancellationToken);
            dict.TryGetValue(puesto.RolSugeridoId.Value, out rolNombre);
        }

        string? departamentoNombre = null;
        if (puesto.DepartamentoId.HasValue)
        {
            departamentoNombre = await _db.Departamentos.AsNoTracking()
                .Where(d => d.Id == puesto.DepartamentoId.Value)
                .Select(d => d.Nombre)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new PuestoResponse(
            puesto.Id,
            puesto.Clave,
            puesto.Nombre,
            puesto.Estatus,
            puesto.Version,
            puesto.RolSugeridoId,
            rolNombre,
            puesto.DepartamentoId,
            departamentoNombre);
    }
}
