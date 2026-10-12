using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Catalogos.Domain;
using Millet.Administracion.Domain;

namespace Millet.Identidad.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IUsuarioSucursalReadPort"/>
/// declarado en <c>Compartido.Application.Administracion.Abstractions</c>
/// (F1-ADM-01 Fase 2). Resuelve la pertenencia usuario→sucursal leyendo
/// <c>identidad.usuario_sucursales</c> vía <see cref="IdentidadDbContext"/>.
///
/// <para>
/// Vive en Identidad —el owner del dato— porque Compartido no puede
/// referenciar Identidad (cerraría el ciclo: Identidad ya referencia
/// Compartido). Se cablea en <c>Program.cs</c>, mismo patrón que
/// <c>UsuarioReadAdapter</c>.
/// </para>
/// </summary>
public sealed class UsuarioSucursalReadAdapter : IUsuarioSucursalReadPort
{
    private readonly IdentidadDbContext _db;

    public UsuarioSucursalReadAdapter(IdentidadDbContext db) => _db = db;

    private IQueryable<Guid> EmpresasCorporativas(Guid usuarioId) =>
        _db.UsuarioEmpresaAlcances.Where(a => a.UsuarioId == usuarioId && a.EsCorporativo &&
            _db.Usuarios.Any(u => u.Id == usuarioId && u.Activo) &&
            _db.UsuarioEmpresaRoles.Any(r => r.UsuarioId == usuarioId && r.EmpresaId == a.EmpresaId &&
                _db.Roles.Any(rol => rol.Id == r.RolId && rol.Activo)))
            .Select(a => a.EmpresaId);

    public Task<bool> EsCorporativoAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        EmpresasCorporativas(usuarioId).AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var empresas = EmpresasCorporativas(usuarioId);
        var corporativas = _db.Set<Sucursal>().Where(s => empresas.Contains(s.EmpresaId)).Select(s => s.Id);
        var asignadas = from a in _db.UsuarioSucursales
                        join s in _db.Set<Sucursal>() on a.SucursalId equals s.Id
                        where a.UsuarioId == usuarioId && a.Estatus == EstatusCatalogo.Activo
                        select a.SucursalId;
        return await corporativas.Union(asignadas).ToListAsync(cancellationToken);
    }

    public async Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken cancellationToken)
    {
        var empresas = EmpresasCorporativas(usuarioId);
        return await _db.Set<Sucursal>().AnyAsync(s => s.Id == sucursalId &&
            (empresas.Contains(s.EmpresaId) || _db.UsuarioSucursales.Any(a => a.UsuarioId == usuarioId &&
                a.SucursalId == sucursalId && a.Estatus == EstatusCatalogo.Activo)), cancellationToken);
    }
}
