using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.UsuarioSucursales;

/// <summary>
/// Asigna un usuario a una sucursal (F1-ADM-01 Fase 2). Análogo de
/// <c>AsignarDepartamentoASucursalCommand</c> pero para
/// <see cref="UsuarioSucursal"/>. La asignación nace Activa.
///
/// <para>
/// Validación de negocio específica del plan: la sucursal debe
/// pertenecer a una empresa donde el usuario tiene rol vía
/// <see cref="UsuarioEmpresaRol"/> — si no, 409 <c>RELACION_INVALIDA</c>.
/// </para>
///
/// <para>
/// Si ya existe una asignación para el par (<see cref="UsuarioId"/>,
/// <see cref="SucursalId"/>) — Activa o Inactiva — 409
/// <c>USUARIO_SUCURSAL_DUPLICADA</c>. Para reactivar una asignación
/// inactiva se usa el endpoint Reactivar.
/// </para>
/// </summary>
public sealed record AsignarUsuarioASucursalCommand(
    Guid SucursalId,
    Guid UsuarioId) : IRequest<UsuarioSucursalResponse>;

public sealed class AsignarUsuarioASucursalValidator
    : AbstractValidator<AsignarUsuarioASucursalCommand>
{
    public AsignarUsuarioASucursalValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty().WithErrorCode("SUCURSAL_REQUERIDA");
        RuleFor(c => c.UsuarioId).NotEmpty().WithErrorCode("USUARIO_REQUERIDO");
    }
}

public sealed class AsignarUsuarioASucursalHandler
    : IRequestHandler<AsignarUsuarioASucursalCommand, UsuarioSucursalResponse>
{
    private readonly IdentidadDbContext _db;

    public AsignarUsuarioASucursalHandler(IdentidadDbContext db) => _db = db;

    public async Task<UsuarioSucursalResponse> Handle(
        AsignarUsuarioASucursalCommand command, CancellationToken cancellationToken)
    {
        var sucursal = await _db.Set<Sucursal>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SucursalId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{command.SucursalId}'.");

        var usuario = await _db.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == command.UsuarioId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_NO_ENCONTRADO",
                $"No existe usuario con id '{command.UsuarioId}'.");

        var tieneRolEnEmpresa = await _db.UsuarioEmpresaRoles.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(
                uer => uer.UsuarioId == command.UsuarioId && uer.EmpresaId == sucursal.EmpresaId,
                cancellationToken);
        if (!tieneRolEnEmpresa)
        {
            throw new ConflictException(
                "RELACION_INVALIDA",
                "El usuario no tiene un rol asignado en la empresa dueña de la sucursal.");
        }

        var existe = await _db.UsuarioSucursales.AsNoTracking()
            .AnyAsync(
                a => a.UsuarioId == command.UsuarioId && a.SucursalId == command.SucursalId,
                cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "USUARIO_SUCURSAL_DUPLICADA",
                $"El usuario '{usuario.Email}' ya está asignado a la sucursal '{sucursal.Clave}'.");
        }

        var asignacion = new UsuarioSucursal(
            Guid.CreateVersion7(),
            command.UsuarioId,
            command.SucursalId,
            sucursal.EmpresaId);

        _db.UsuarioSucursales.Add(asignacion);
        await _db.SaveChangesAsync(cancellationToken);

        return new UsuarioSucursalResponse(
            asignacion.SucursalId,
            asignacion.UsuarioId,
            usuario.Email,
            usuario.Nombre,
            asignacion.Estatus,
            asignacion.Version);
    }
}
