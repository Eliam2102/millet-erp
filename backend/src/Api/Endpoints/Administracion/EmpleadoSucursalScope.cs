using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Alcance por sucursal (ADR-0051) de las operaciones sobre un empleado:
/// alta, edición, baja/reactivación y acceso al ERP. Complementa el permiso
/// declarativo <c>admin.empleados.gestionar</c> (qué puede hacer) con dónde
/// puede hacerlo: la persona operativa sólo actúa sobre empleados de las
/// sucursales a las que está asociada (<c>UsuarioSucursal</c>); la persona
/// corporativa necesita <c>admin.empleados.gestionar-todas-sucursales</c>.
///
/// <para>
/// Se evalúa en el endpoint, antes de enviar el comando, para que un
/// actor fuera de alcance reciba 403 y no un 409/422 de reglas de negocio
/// que revelaría datos del empleado ajeno. Un empleado sin sucursal laboral
/// sólo lo gestiona quien tiene el bypass (mismo criterio que el catálogo,
/// que tampoco lo muestra a la persona operativa).
/// </para>
/// </summary>
internal static class EmpleadoSucursalScope
{
    public static async Task VerificarEmpleadoAsync(
        Guid empleadoId,
        CompartidoDbContext db,
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioSucursalReadPort usuarioSucursales,
        CancellationToken ct)
    {
        if (await TieneBypassAsync(permisos, ct)) return;

        var empleado = await db.Empleados.AsNoTracking()
            .Where(e => e.Id == empleadoId)
            .Select(e => new { e.SucursalId })
            .FirstOrDefaultAsync(ct);

        // Inexistente: el handler responde 404 con su código habitual.
        if (empleado is null) return;

        await VerificarSucursalAsync(
            empleado.SucursalId, currentUser, permisos, usuarioSucursales, ct);
    }

    public static async Task VerificarSucursalAsync(
        Guid? sucursalId,
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioSucursalReadPort usuarioSucursales,
        CancellationToken ct)
    {
        if (await TieneBypassAsync(permisos, ct)) return;

        if (sucursalId is not Guid sid)
        {
            throw new ForbiddenException(
                "SUCURSAL_NO_ASOCIADA",
                "Sólo quien gestiona empleados de todas las sucursales puede operar empleados sin sucursal.");
        }

        await SucursalScopeGuard.VerificarAsync(
            currentUser.UserId,
            PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales,
            permisos,
            (uid, token) => usuarioSucursales.EstaAsociadoAsync(uid, sid, token),
            ct);
    }

    private static async Task<bool> TieneBypassAsync(ICurrentUserPermissions permisos, CancellationToken ct) =>
        await permisos.TieneAsync(PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales, ct);
}
