using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Ports;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador de <see cref="ISucursalContabilidadPort"/> (F1-CON-02). Compartido es dueño de <c>compartido.sucursales</c>;
/// el filtro global por empresa (ADR-0011) limita a la empresa del request. La pertenencia usuario ↔ sucursal se delega en
/// <see cref="IUsuarioSucursalReadPort"/> (Identidad), la misma fuente que usa <c>SucursalScopeGuard</c> (ADR-0051).
/// </summary>
public sealed class SucursalContabilidadAdapter(CompartidoDbContext db, IUsuarioSucursalReadPort usuarioSucursales) : ISucursalContabilidadPort
{
    public async Task<IReadOnlyList<SucursalContable>> ListarAsync(CancellationToken ct) =>
        await db.Sucursales.AsNoTracking()
            .OrderBy(s => s.Clave)
            .Select(s => new SucursalContable(s.Id, s.Clave, s.Nombre, s.Estatus == EstatusCatalogo.Activo))
            .ToListAsync(ct);

    public Task<bool> UsuarioAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken ct) =>
        usuarioSucursales.EstaAsociadoAsync(usuarioId, sucursalId, ct);
}
