using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Cajas.Alcance;

/// <summary>
/// Implementación de <see cref="IAlcanceCajaEvaluator"/> sobre las tablas de
/// configuración del módulo (<c>caja</c> × relaciones + <c>usuario_alcance</c>).
/// El filtro multi-tenant de empresa lo aplica el query filter global del
/// <see cref="FacturacionDbContext"/>.
/// </summary>
public sealed class AlcanceCajaEvaluator : IAlcanceCajaEvaluator
{
    /// <summary>
    /// Espejo de <c>PermisosCanonicos.FacturacionCajaLeerTodas</c> (Identidad).
    /// Facturación no referencia Identidad; el código canónico es contrato
    /// estable (ADR-0007, seed 00000009-0009-...0005).
    /// </summary>
    internal const string PermisoLeerTodas = "facturacion.caja.leer-todas";

    private readonly FacturacionDbContext _db;
    private readonly ICurrentUserContext _user;
    private readonly ICurrentUserPermissions _permisos;

    public AlcanceCajaEvaluator(
        FacturacionDbContext db,
        ICurrentUserContext user,
        ICurrentUserPermissions permisos)
    {
        _db = db;
        _user = user;
        _permisos = permisos;
    }

    public async Task<AlcanceCajas> ResolverAsync(CancellationToken cancellationToken)
    {
        if (await _permisos.TieneAsync(PermisoLeerTodas, cancellationToken))
            return AlcanceCajas.Total();

        if (_user.UserId is not Guid usuarioId)
            return AlcanceCajas.Ninguno();

        var cajas = await _db.Cajas.AsNoTracking()
            .Where(c => c.Estatus == EstatusCatalogo.Activo
                        && c.Usuarios.Any(u => u.UsuarioId == usuarioId))
            .Select(c => new
            {
                Sucursales = c.Sucursales.Select(s => s.SucursalId).ToList(),
                Canales = c.Canales.Select(x => x.CanalVentaId).ToList(),
            })
            .ToListAsync(cancellationToken);

        var combinaciones = new HashSet<CombinacionAlcance>();
        foreach (var caja in cajas)
            ExpandirCaja(combinaciones, caja.Sucursales, caja.Canales);

        var concesiones = await _db.UsuariosAlcance.AsNoTracking()
            .Where(a => a.UsuarioId == usuarioId)
            .Select(a => new { a.SucursalId, a.CanalVentaId })
            .ToListAsync(cancellationToken);

        foreach (var concesion in concesiones)
            combinaciones.Add(new CombinacionAlcance(concesion.SucursalId, concesion.CanalVentaId));

        return AlcanceCajas.De([.. combinaciones]);
    }

    public async Task<AlcanceSinAsignar> ResolverSinAsignarAsync(CancellationToken cancellationToken)
    {
        var cajas = await _db.Cajas.AsNoTracking()
            .Where(c => c.Estatus == EstatusCatalogo.Activo)
            .Select(c => new
            {
                Sucursales = c.Sucursales.Select(s => s.SucursalId).ToList(),
                Canales = c.Canales.Select(x => x.CanalVentaId).ToList(),
            })
            .ToListAsync(cancellationToken);

        var combinaciones = new HashSet<CombinacionAlcance>();
        foreach (var caja in cajas)
            ExpandirCaja(combinaciones, caja.Sucursales, caja.Canales);

        return new AlcanceSinAsignar([.. combinaciones]);
    }

    /// <summary>
    /// Expande una caja a sus combinaciones: producto cartesiano sucursales ×
    /// canales; lado vacío = comodín (null) — 12-cajas.md §4.1.
    /// </summary>
    private static void ExpandirCaja(
        HashSet<CombinacionAlcance> destino,
        List<Guid> sucursales,
        List<short> canales)
    {
        if (sucursales.Count == 0 && canales.Count == 0)
        {
            destino.Add(new CombinacionAlcance(null, null));
            return;
        }

        if (sucursales.Count == 0)
        {
            foreach (var canal in canales)
                destino.Add(new CombinacionAlcance(null, canal));
            return;
        }

        if (canales.Count == 0)
        {
            foreach (var sucursal in sucursales)
                destino.Add(new CombinacionAlcance(sucursal, null));
            return;
        }

        foreach (var sucursal in sucursales)
            foreach (var canal in canales)
                destino.Add(new CombinacionAlcance(sucursal, canal));
    }
}
