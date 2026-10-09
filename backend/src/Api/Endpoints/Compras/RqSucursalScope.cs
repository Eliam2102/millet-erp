using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Compras;

/// <summary>
/// Alcance por sucursal (ADR-0051) para lecturas por id de una requisición.
/// La sucursal se deriva de <see cref="Millet.Compras.Domain.Requisicion.SucursalId"/> en BD,
/// nunca de un parámetro del cliente. Mismo patrón que <c>OcSucursalScope</c>.
/// </summary>
internal static class RqSucursalScope
{
    /// <summary>
    /// Lanza <see cref="EntityNotFoundException"/> (404) si la requisición no existe
    /// y <see cref="ForbiddenException"/> (403 SUCURSAL_NO_ASOCIADA) si el usuario no tiene
    /// el bypass ni está asociado a la sucursal de la requisición.
    /// </summary>
    public static async Task VerificarAsync(
        Guid requisicionId,
        ComprasDbContext db,
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioSucursalReadPort usuarioSucursales,
        CancellationToken ct)
    {
        var sucursalId = await db.Requisiciones.AsNoTracking()
            .Where(r => r.Id == requisicionId)
            .Select(r => (Guid?)r.SucursalId)
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{requisicionId}'.");

        await SucursalScopeGuard.VerificarAsync(
            currentUser.UserId,
            SucursalScopeGuardPermisos.RequisicionesLeerTodas,
            permisos,
            (uid, c) => usuarioSucursales.EstaAsociadoAsync(uid, sucursalId, c),
            ct);
    }
}
