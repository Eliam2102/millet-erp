using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IUnidadMedidaReadPort"/> (ADR-0046
/// Etapa 2). Resuelve en batch <c>articuloId → decimales de su unidad</c>
/// con un JOIN <c>articulos ⨝ unidades_medida</c> sobre
/// <see cref="CompartidoDbContext"/> (<c>AsNoTracking</c>).
///
/// <para>Usa <c>ICurrentEmpresaContext.Bypass()</c> porque ambos catálogos
/// son cross-empresa (mismo patrón que <see cref="ArticuloReadAdapter"/>).
/// Devuelve <c>null</c> cuando el artículo no tiene <c>unidad_medida_id</c>
/// (FK NULL → la validación se omite aguas arriba); los artículos
/// inexistentes no aparecen en el diccionario.</para>
/// </summary>
public sealed class UnidadMedidaReadAdapter : IUnidadMedidaReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public UnidadMedidaReadAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyDictionary<Guid, int?>> ObtenerDecimalesPorArticulosAsync(
        IEnumerable<Guid> articuloIds,
        CancellationToken cancellationToken)
    {
        var distinct = articuloIds.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            return new Dictionary<Guid, int?>();
        }

        using var bypass = _empresaContext.Bypass();

        var filas = await _db.Articulos
            .AsNoTracking()
            .Where(a => distinct.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                Decimales = _db.UnidadesMedida
                    .Where(u => u.Id == a.UnidadMedidaId)
                    .Select(u => (int?)u.Decimales)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return filas.ToDictionary(f => f.Id, f => f.Decimales);
    }
}
