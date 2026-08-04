using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Reactiva un usuario previamente desactivado (F-Admin-PR4.2).
/// Idempotente: si ya está activo, no-op. 404 si no existe.
/// </summary>
public sealed record ReactivarUsuarioCommand(Guid Id) : IRequest<UsuarioResponse>;

public sealed class ReactivarUsuarioHandler
    : IRequestHandler<ReactivarUsuarioCommand, UsuarioResponse>
{
    private readonly IdentidadDbContext _db;

    public ReactivarUsuarioHandler(IdentidadDbContext db) => _db = db;

    public async Task<UsuarioResponse> Handle(
        ReactivarUsuarioCommand command, CancellationToken cancellationToken)
    {
        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_NO_ENCONTRADO",
                $"No existe usuario con id '{command.Id}'.");

        if (!usuario.Activo)
        {
            usuario.Reactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new UsuarioResponse(
            usuario.Id,
            usuario.Email,
            usuario.EntraOid,
            usuario.Nombre,
            usuario.DepartamentoId,
            usuario.Activo,
            usuario.Version);
    }
}
