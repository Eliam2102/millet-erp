using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compras.Application.Listar;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
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
        CancellationToken ct, bool escritura = false)
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
            escritura ? "compras.requisiciones.gestionar-todas-sucursales" : SucursalScopeGuardPermisos.RequisicionesLeerTodas,
            permisos,
            (uid, c) => usuarioSucursales.EstaAsociadoAsync(uid, sucursalId, c),
            ct);
    }
}

/// <summary>
/// Alcance por departamento de las requisiciones (F-RQOC-01). Sin
/// <c>compras.requisiciones.ver-todos-departamentos</c> el usuario solo ve las RQ de su departamento
/// y las que pidió o capturó. Compras (crear OC) y Almacén (registrar salidas) atienden RQ de todos
/// los departamentos por su función; quien firma RQ además abre el detalle de las que le tocan.
/// </summary>
internal static class RqDepartamentoScope
{
    private static readonly string[] Transversales =
    [
        PermisosCanonicos.ComprasRequisicionesVerTodosDepartamentos,
        PermisosCanonicos.ComprasOrdenesCrear,
        PermisosCanonicos.AlmacenSalidasRegistrar,
    ];

    private static readonly string[] Firmantes =
    [
        PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
        PermisosCanonicos.ComprasRequisicionesAutorizarNivel2,
    ];

    /// <summary>Null si el usuario ve todos los departamentos.</summary>
    public static async Task<AlcanceDepartamentoRq?> ResolverAsync(
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioReadPort usuarios,
        bool detalle,
        CancellationToken ct)
    {
        foreach (var permiso in detalle ? [.. Transversales, .. Firmantes] : Transversales)
        {
            if (await permisos.TieneAsync(permiso, ct)) return null;
        }
        var usuarioId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Sin usuario autenticado.");
        return new AlcanceDepartamentoRq(usuarioId, await usuarios.ObtenerDepartamentoAsync(usuarioId, ct));
    }

    /// <summary>403 DEPARTAMENTO_NO_ASOCIADO si la RQ es de otro departamento y no es suya.</summary>
    public static async Task VerificarAsync(
        Guid requisicionId,
        ComprasDbContext db,
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioReadPort usuarios,
        CancellationToken ct)
    {
        if (await ResolverAsync(currentUser, permisos, usuarios, detalle: true, ct) is not { } alcance) return;
        var visible = await db.Requisiciones.AsNoTracking()
            .Where(r => r.Id == requisicionId)
            .AnyAsync(alcance.Visible(), ct);
        if (!visible)
        {
            throw new ForbiddenException(
                "DEPARTAMENTO_NO_ASOCIADO",
                "Esta requisición es de otro departamento; solo puedes ver las de tu departamento o las que tú pediste.");
        }
    }
}
