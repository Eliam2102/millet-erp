using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Catalogos.Domain;

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

    public async Task<bool> EstaAsociadoAsync(
        Guid usuarioId, Guid sucursalId, CancellationToken cancellationToken)
    {
        return await _db.UsuarioSucursales
            .AsNoTracking()
            .AnyAsync(
                a => a.UsuarioId == usuarioId
                  && a.SucursalId == sucursalId
                  && a.Estatus == EstatusCatalogo.Activo,
                cancellationToken);
    }
}
