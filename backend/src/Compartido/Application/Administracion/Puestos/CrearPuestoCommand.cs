using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Puestos;

/// <summary>
/// Crea un puesto (ADM-PR1, F1-ADM-01.4). UNIQUE(clave) → 409
/// <c>PUESTO_CLAVE_DUPLICADA</c>. RolSugeridoId inválido → 409 <c>ROL_SUGERIDO_INVALIDO</c>.
/// </summary>
public sealed record CrearPuestoCommand(
    Guid Id,
    string Clave,
    string Nombre,
    Guid? RolSugeridoId = null,
    Guid? DepartamentoId = null) : IRequest<PuestoResponse>;

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
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly IRolReadPort _rolReadPort;

    public CrearPuestoHandler(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext,
        IRolReadPort rolReadPort)
    {
        _db = db;
        _empresaContext = empresaContext;
        _rolReadPort = rolReadPort;
    }

    public async Task<PuestoResponse> Handle(
        CrearPuestoCommand command, CancellationToken cancellationToken)
    {
        if (_empresaContext.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var claveExiste = await _db.Puestos.AsNoTracking()
            .AnyAsync(p => p.EmpresaId == empresaId && p.Clave == command.Clave, cancellationToken);
        if (claveExiste)
        {
            throw new ConflictException(
                "PUESTO_CLAVE_DUPLICADA",
                $"Ya existe un puesto con clave '{command.Clave}'.");
        }

        string? rolNombre = null;
        if (command.RolSugeridoId.HasValue)
        {
            var rolActivo = await _rolReadPort.ExisteActivoAsync(command.RolSugeridoId.Value, cancellationToken);
            if (!rolActivo)
            {
                throw new ConflictException(
                    "ROL_SUGERIDO_INVALIDO",
                    $"El rol sugerido '{command.RolSugeridoId.Value}' no existe o está inactivo.");
            }

            var dict = await _rolReadPort.ObtenerNombresPorIdsAsync(new[] { command.RolSugeridoId.Value }, cancellationToken);
            dict.TryGetValue(command.RolSugeridoId.Value, out rolNombre);
        }

        string? departamentoNombre = null;
        if (command.DepartamentoId.HasValue)
        {
            var depto = await _db.Departamentos.AsNoTracking()
                .FirstOrDefaultAsync(d => d.EmpresaId == empresaId && d.Id == command.DepartamentoId.Value, cancellationToken);
            if (depto is null)
            {
                throw new ConflictException(
                    "DEPARTAMENTO_INVALIDO",
                    $"El departamento '{command.DepartamentoId.Value}' no existe en esta empresa.");
            }
            departamentoNombre = depto.Nombre;
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        var puesto = new Puesto(
            id,
            empresaId,
            command.Clave,
            command.Nombre,
            rolSugeridoId: command.RolSugeridoId,
            departamentoId: command.DepartamentoId);

        _db.Puestos.Add(puesto);
        await _db.SaveChangesAsync(cancellationToken);

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

