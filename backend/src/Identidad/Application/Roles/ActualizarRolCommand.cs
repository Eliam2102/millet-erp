using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Roles;

/// <summary>
/// PATCH parcial sobre un rol existente (F-Admin-PR3.2). Convención del
/// repo: campos <c>null</c> en el command = no tocar; para limpiar
/// <see cref="Descripcion"/> a <c>null</c> se usa
/// <see cref="LimpiarDescripcion"/> = <c>true</c>.
///
/// <para>404 si el rol no existe; 422
/// <c>ROL_DEL_SISTEMA_NO_EDITABLE</c> si <c>EsDelSistema</c>.</para>
/// </summary>
public sealed record ActualizarRolCommand(
    Guid Id,
    string? Nombre,
    string? Descripcion,
    bool LimpiarDescripcion) : IRequest<RolResponse>;

public sealed class ActualizarRolValidator : AbstractValidator<ActualizarRolCommand>
{
    public ActualizarRolValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(100)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.Descripcion!).MaximumLength(500)
            .When(c => c.Descripcion is not null);
    }
}

public sealed class ActualizarRolHandler
    : IRequestHandler<ActualizarRolCommand, RolResponse>
{
    private readonly IdentidadDbContext _db;

    public ActualizarRolHandler(IdentidadDbContext db) => _db = db;

    public async Task<RolResponse> Handle(
        ActualizarRolCommand command, CancellationToken cancellationToken)
    {
        var rol = await _db.Roles
            .FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ROL_NO_ENCONTRADO",
                $"No existe rol con id '{command.Id}'.");

        if (rol.EsDelSistema)
        {
            throw new BusinessRuleException(
                "ROL_DEL_SISTEMA_NO_EDITABLE",
                "Los roles del sistema (super-admin, etc.) no se pueden editar.");
        }

        // El dominio Rol.ActualizarMetadatos requiere nombre obligatorio.
        // En PATCH parcial dejamos el nombre actual si el caller no lo envió;
        // misma decisión para Descripcion respetando LimpiarDescripcion.
        var nuevoNombre = command.Nombre ?? rol.Nombre;
        var nuevaDescripcion = command.LimpiarDescripcion
            ? null
            : command.Descripcion ?? rol.Descripcion;

        rol.ActualizarMetadatos(nuevoNombre, nuevaDescripcion);
        await _db.SaveChangesAsync(cancellationToken);

        return new RolResponse(
            rol.Id, rol.Codigo, rol.Nombre, rol.Descripcion,
            rol.EsDelSistema, rol.Activo, rol.Version);
    }
}
