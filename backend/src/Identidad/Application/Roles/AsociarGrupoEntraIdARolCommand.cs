using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Roles;

/// <summary>
/// Asocia un grupo de Microsoft Entra ID a un rol del sistema
/// (F-Admin-PR3.2, A3=a). Usa la factory <c>Rol.AsociarGrupoEntraId(...)</c>
/// del dominio.
///
/// <list type="bullet">
///   <item>404 <c>ROL_NO_ENCONTRADO</c> si el rol no existe.</item>
///   <item>409 <c>ROL_GRUPO_ENTRAID_DUPLICADO</c> si ya hay una asociación
///         <c>(RolId, ObjectId)</c> (unique index del dominio).</item>
/// </list>
/// </summary>
public sealed record AsociarGrupoEntraIdARolCommand(
    Guid RolId,
    string ObjectId,
    string Nombre) : IRequest<RolGrupoEntraIdResponse>;

public sealed class AsociarGrupoEntraIdARolValidator
    : AbstractValidator<AsociarGrupoEntraIdARolCommand>
{
    public AsociarGrupoEntraIdARolValidator()
    {
        RuleFor(c => c.RolId).NotEmpty();
        RuleFor(c => c.ObjectId).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
    }
}

public sealed class AsociarGrupoEntraIdARolHandler
    : IRequestHandler<AsociarGrupoEntraIdARolCommand, RolGrupoEntraIdResponse>
{
    private readonly IdentidadDbContext _db;

    public AsociarGrupoEntraIdARolHandler(IdentidadDbContext db) => _db = db;

    public async Task<RolGrupoEntraIdResponse> Handle(
        AsociarGrupoEntraIdARolCommand command, CancellationToken cancellationToken)
    {
        var rol = await _db.Roles
            .FirstOrDefaultAsync(r => r.Id == command.RolId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ROL_NO_ENCONTRADO",
                $"No existe rol con id '{command.RolId}'.");

        var duplicado = await _db.RolGruposEntraId.AsNoTracking()
            .AnyAsync(rge => rge.RolId == command.RolId && rge.ObjectId == command.ObjectId,
                cancellationToken);
        if (duplicado)
        {
            throw new ConflictException(
                "ROL_GRUPO_ENTRAID_DUPLICADO",
                $"El grupo Entra ID '{command.ObjectId}' ya está asociado al rol.");
        }

        var asociacion = rol.AsociarGrupoEntraId(command.ObjectId, command.Nombre);
        _db.RolGruposEntraId.Add(asociacion);
        await _db.SaveChangesAsync(cancellationToken);

        return new RolGrupoEntraIdResponse(
            asociacion.Id, asociacion.RolId, asociacion.ObjectId, asociacion.Nombre);
    }
}
