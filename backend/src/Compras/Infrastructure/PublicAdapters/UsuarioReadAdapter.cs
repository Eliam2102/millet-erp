using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IUsuarioReadPort"/> (ADR-0042).
/// Resuelve <c>usuarioId → nombre</c> leyendo <c>identidad.usuarios</c> via
/// <see cref="IdentidadDbContext"/>.
///
/// <para>
/// Vive en Compras (no en Identidad) porque <c>Millet.Identidad.csproj</c>
/// no referencia módulos de negocio (evita ciclos); el adapter del
/// consumidor lee del DbContext del data-owner, mismo patrón que
/// <see cref="AlmacenReadAdapter"/> y <see cref="SucursalDepartamentoReadAdapter"/>.
/// </para>
///
/// <para>
/// <c>AsNoTracking</c> + <c>ICurrentEmpresaContext.Bypass()</c> — el padrón
/// de usuarios es cross-empresa (una persona puede operar en varias
/// empresas); el bypass evita que el filtro global recorte la resolución.
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
