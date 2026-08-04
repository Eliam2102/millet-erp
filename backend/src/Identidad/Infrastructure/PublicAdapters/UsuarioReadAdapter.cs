using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IUsuarioReadPort"/> declarado en
/// <c>Almacen.Domain.Ports</c> (ADR-0042). Resuelve <c>usuarioId → nombre</c>
/// leyendo <c>identidad.usuarios</c> via <see cref="IdentidadDbContext"/>.
///
/// <para>
/// A diferencia del <c>UsuarioReadAdapter</c> de Compras (que vive en el
/// consumidor porque Identidad no referencia Compras), este adapter vive en
/// <b>Identidad</b> —el owner del dato—: Almacén y Compartido no pueden
/// hospedarlo porque Identidad ya los referencia (seed de permisos canónicos)
/// y la referencia inversa cerraría ciclo. Identidad ya referencia
/// <c>Almacen.Domain</c>, así que implementar su puerto no agrega acoplamiento
/// nuevo. Se cablea en <c>Program.cs</c>.
/// </para>
///
/// <para>
/// <c>AsNoTracking</c> + <c>ICurrentEmpresaContext.Bypass()</c> — el padrón de
/// usuarios es cross-empresa (una persona puede operar en varias empresas); el
/// bypass evita que el filtro global recorte la resolución. Mismo patrón que
/// el adapter hermano de Compras.
/// </para>
/// </summary>
public sealed class UsuarioReadAdapter : IUsuarioReadPort
{
    private readonly IdentidadDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public UsuarioReadAdapter(
        IdentidadDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken)
    {
        if (usuarioIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var distinct = usuarioIds.Distinct().ToArray();

        using var bypass = _empresaContext.Bypass();

        return await _db.Usuarios
            .AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nombre, cancellationToken);
    }
}
