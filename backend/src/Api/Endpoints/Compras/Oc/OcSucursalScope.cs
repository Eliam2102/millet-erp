using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Compras.Oc;

/// <summary>
/// Alcance por sucursal (ADR-0051) para operaciones sobre órdenes de compra y sus adjuntos.
/// La sucursal se deriva de <see cref="Millet.Compras.Domain.Oc.OrdenCompra.SucursalDestinoId"/> en BD,
/// garantizando que la autorización se compruebe contra el documento padre y no contra
/// un parámetro manipulable del cliente.
/// </summary>
internal static class OcSucursalScope
{
    /// <summary>
    /// Verifica que el usuario tenga acceso a la orden de compra especificada según su sucursal de destino.
    /// Lanza <see cref="EntityNotFoundException"/> (404) si la orden de compra no existe o pertenece a otra empresa.
    /// Lanza <see cref="ForbiddenException"/> (403 SUCURSAL_NO_ASOCIADA) si el usuario no tiene bypass ni está asociado a la sucursal.
    /// </summary>
    public static async Task VerificarAsync(
        Guid ordenCompraId,
        ComprasDbContext db,
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioSucursalReadPort usuarioSucursales,
        CancellationToken ct)
    {
        var sucursalId = await db.OrdenesCompra.AsNoTracking()
            .Where(o => o.Id == ordenCompraId)
            .Select(o => (Guid?)o.SucursalDestinoId)
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{ordenCompraId}'.");

        await SucursalScopeGuard.VerificarAsync(
            currentUser.UserId,
            SucursalScopeGuardPermisos.OrdenesCompraLeerTodas,
            permisos,
            (uid, c) => usuarioSucursales.EstaAsociadoAsync(uid, sucursalId, c),
            ct);
    }
}
