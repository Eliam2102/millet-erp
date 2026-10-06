using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Ports;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador de <see cref="ITerceroContabilidadPort"/> (F1-CON-02): clientes y proveedores de los maestros de Compartido como
/// dimensiones auxiliares de una partida. Incluye inactivos al resolver por id (para mostrar y para rechazar con motivo).
/// </summary>
public sealed class TerceroContabilidadAdapter(CompartidoDbContext db) : ITerceroContabilidadPort
{
    public async Task<IReadOnlyDictionary<Guid, AuxiliarContable>> ObtenerAsync(TipoAuxiliar tipo, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<Guid, AuxiliarContable>();
        var arr = ids.Distinct().ToArray();
        return tipo switch
        {
            TipoAuxiliar.Cliente => await db.Clientes.AsNoTracking().Where(c => arr.Contains(c.Id))
                .Select(c => new AuxiliarContable(c.Id, c.Clave, c.RazonSocial, c.Estatus == EstatusCatalogo.Activo)).ToDictionaryAsync(a => a.Id, ct),
            TipoAuxiliar.Proveedor => await db.Proveedores.AsNoTracking().Where(p => arr.Contains(p.Id))
                .Select(p => new AuxiliarContable(p.Id, p.Clave, p.RazonSocial, p.Estatus == EstatusCatalogo.Activo)).ToDictionaryAsync(a => a.Id, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Solo clientes y proveedores."),
        };
    }

    public async Task<IReadOnlyList<AuxiliarContable>> BuscarAsync(TipoAuxiliar tipo, string? q, int limite, CancellationToken ct)
    {
        var patron = string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";
        return tipo switch
        {
            TipoAuxiliar.Cliente => await db.Clientes.AsNoTracking()
                .Where(c => c.Estatus == EstatusCatalogo.Activo)
                .Where(c => patron == null || EF.Functions.ILike(c.Clave, patron) || EF.Functions.ILike(c.RazonSocial, patron) || (c.Rfc != null && EF.Functions.ILike(c.Rfc, patron)))
                .OrderBy(c => c.RazonSocial).Take(limite)
                .Select(c => new AuxiliarContable(c.Id, c.Clave, c.RazonSocial, true)).ToListAsync(ct),
            TipoAuxiliar.Proveedor => await db.Proveedores.AsNoTracking()
                .Where(p => p.Estatus == EstatusCatalogo.Activo)
                .Where(p => patron == null || EF.Functions.ILike(p.Clave, patron) || EF.Functions.ILike(p.RazonSocial, patron) || EF.Functions.ILike(p.Rfc, patron))
                .OrderBy(p => p.RazonSocial).Take(limite)
                .Select(p => new AuxiliarContable(p.Id, p.Clave, p.RazonSocial, true)).ToListAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Solo clientes y proveedores."),
        };
    }
}
