using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// Asigna un puesto a un departamento de una sucursal (F1-ADM-01 Fase 2,
/// reabierta 2026-09-24 por pedido del owner: un mismo puesto puede
/// asignarse a varios departamentos de la sucursal). Análogo de
/// <c>AsignarDepartamentoASucursalCommand</c>. La asignación nace Activa.
/// Si ya existe una asignación para la terna (<see cref="SucursalId"/>,
/// <see cref="PuestoId"/>, <see cref="DepartamentoId"/>) — sea Activa o
/// Inactiva — devuelve 409 <c>SUCURSAL_PUESTO_DUPLICADA</c>. El mismo
/// puesto en OTRO departamento activo de la sucursal es una asignación
/// distinta y válida. Para reactivar una asignación inactiva se usa el
/// endpoint Reactivar.
///
/// <para>
/// <see cref="RolSugeridoId"/> opcional (excepción puntual sobre el rol
/// sugerido del puesto, F1-ADM-01.4 reabierta): si viene, debe ser un rol
/// existente y activo → 409 <c>ROL_SUGERIDO_INVALIDO</c> (mismo patrón
/// que <c>CrearPuestoCommand</c>).
/// </para>
/// </summary>
public sealed record AsignarPuestoASucursalCommand(
    Guid SucursalId,
    Guid PuestoId,
    Guid DepartamentoId,
    Guid? RolSugeridoId = null) : IRequest<SucursalPuestoResponse>;

public sealed class AsignarPuestoASucursalValidator
    : AbstractValidator<AsignarPuestoASucursalCommand>
{
    public AsignarPuestoASucursalValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty().WithErrorCode("SUCURSAL_REQUERIDA");
        RuleFor(c => c.PuestoId).NotEmpty().WithErrorCode("PUESTO_REQUERIDO");
        RuleFor(c => c.DepartamentoId).NotEmpty().WithErrorCode("DEPARTAMENTO_REQUERIDO");
    }
}

public sealed class AsignarPuestoASucursalHandler
    : IRequestHandler<AsignarPuestoASucursalCommand, SucursalPuestoResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly IRolReadPort _rolReadPort;

    public AsignarPuestoASucursalHandler(CompartidoDbContext db, IRolReadPort rolReadPort)
    {
        _db = db;
        _rolReadPort = rolReadPort;
    }

    public async Task<SucursalPuestoResponse> Handle(
        AsignarPuestoASucursalCommand command, CancellationToken cancellationToken)
    {
        var sucursal = await _db.Sucursales.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SucursalId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{command.SucursalId}'.");

        var puesto = await _db.Puestos.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PuestoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PUESTO_NO_ENCONTRADO",
                $"No existe puesto con id '{command.PuestoId}'.");

        var depto = await _db.Departamentos.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == command.DepartamentoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "DEPARTAMENTO_NO_ENCONTRADO",
                $"No existe departamento con id '{command.DepartamentoId}'.");

        if (sucursal.EmpresaId != puesto.EmpresaId)
        {
            throw new ConflictException(
                "RELACION_INVALIDA",
                "La sucursal y el puesto deben pertenecer a la misma empresa.");
        }

        if (sucursal.EmpresaId != depto.EmpresaId)
        {
            throw new ConflictException(
                "DEPARTAMENTO_OTRA_EMPRESA",
                "El departamento debe pertenecer a la misma empresa.");
        }

        if (depto.Estatus != EstatusCatalogo.Activo)
        {
            throw new ConflictException(
                "DEPARTAMENTO_INACTIVO",
                "El departamento no está activo.");
        }

        var deptoAsignado = await _db.SucursalDepartamentos.AsNoTracking()
            .FirstOrDefaultAsync(
                sd => sd.SucursalId == command.SucursalId && sd.DepartamentoId == command.DepartamentoId,
                cancellationToken);
        if (deptoAsignado is null)
        {
            throw new ConflictException(
                "DEPARTAMENTO_NO_ASIGNADO_A_SUCURSAL",
                $"El departamento '{depto.Nombre}' no está asignado a la sucursal '{sucursal.Nombre}'.");
        }

        if (deptoAsignado.Estatus != EstatusCatalogo.Activo)
        {
            throw new ConflictException(
                "DEPARTAMENTO_SUCURSAL_INACTIVO",
                $"La asignación del departamento '{depto.Nombre}' a la sucursal '{sucursal.Nombre}' está inactiva.");
        }

        if (command.RolSugeridoId.HasValue)
        {
            var rolActivo = await _rolReadPort.ExisteActivoAsync(command.RolSugeridoId.Value, cancellationToken);
            if (!rolActivo)
            {
                throw new ConflictException(
                    "ROL_SUGERIDO_INVALIDO",
                    $"El rol sugerido '{command.RolSugeridoId.Value}' no existe o está inactivo.");
            }
        }

        var existe = await _db.SucursalPuestos.AsNoTracking()
            .AnyAsync(
                a => a.SucursalId == command.SucursalId
                  && a.PuestoId == command.PuestoId
                  && a.DepartamentoId == command.DepartamentoId,
                cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "SUCURSAL_PUESTO_DUPLICADA",
                $"El puesto '{puesto.Clave}' ya está asignado al departamento '{depto.Nombre}' " +
                $"en la sucursal '{sucursal.Clave}'.");
        }

        var asignacion = new SucursalPuesto(
            Guid.CreateVersion7(),
            sucursal.EmpresaId,
            command.SucursalId,
            command.PuestoId,
            command.DepartamentoId,
            rolSugeridoId: command.RolSugeridoId);

        _db.SucursalPuestos.Add(asignacion);
        await _db.SaveChangesAsync(cancellationToken);

        return new SucursalPuestoResponse(
            asignacion.SucursalId,
            asignacion.PuestoId,
            puesto.Clave,
            puesto.Nombre,
            asignacion.DepartamentoId,
            depto.Nombre,
            asignacion.Estatus,
            asignacion.Version,
            asignacion.RolSugeridoId,
            asignacion.RolSugeridoId ?? puesto.RolSugeridoId);
    }
}
