using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IDepartamentoReadPort"/>
/// (ADR-0042). Resuelve <c>departamentoId → (clave, nombre)</c> leyendo
/// <c>compartido.departamentos</c> via <see cref="CompartidoDbContext"/>.
///
/// <para>
/// Mismo patrón y rationale que <see cref="SucursalDepartamentoReadAdapter"/>
/// (el consumidor hospeda el adapter del data-owner). <c>AsNoTracking</c> +
/// <c>Bypass()</c> porque el catálogo organizacional es cross-empresa.
/// </para>
/// </summary>
public sealed class DepartamentoReadAdapter : IDepartamentoReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public DepartamentoReadAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyDictionary<Guid, DepartamentoNombreLectura>> ObtenerAsync(
        IReadOnlyCollection<Guid> departamentoIds,
        CancellationToken cancellationToken)
    {
        if (departamentoIds.Count == 0)
        {
            return new Dictionary<Guid, DepartamentoNombreLectura>();
        }

        var distinct = departamentoIds.Distinct().ToArray();

        using var bypass = _empresaContext.Bypass();

        return await _db.Departamentos
            .AsNoTracking()
            .Where(d => distinct.Contains(d.Id))
            .Select(d => new DepartamentoNombreLectura(d.Id, d.Clave, d.Nombre))
            .ToDictionaryAsync(d => d.Id, d => d, cancellationToken);
    }
}
