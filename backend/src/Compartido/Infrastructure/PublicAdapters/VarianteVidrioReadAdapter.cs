using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>Adaptador de <see cref="IVarianteVidrioReadPort"/> sobre <see cref="CompartidoDbContext"/> (solo lectura).</summary>
public sealed class VarianteVidrioReadAdapter(CompartidoDbContext db) : IVarianteVidrioReadPort
{
    public async Task<VarianteVidrioLectura> ObtenerAsync(Guid articuloId, Guid varianteId, CancellationToken cancellationToken)
    {
        var fila = await (
            from a in db.Articulos.AsNoTracking()
            join p in db.ProductosAw.AsNoTracking() on a.ProductoAwId equals p.Id
            from v in p.Variantes
            where a.Id == articuloId && v.Id == varianteId
            select new { p.Estatus, Variante = new VarianteVidrioLectura(v.Id, v.ClaveVariante, v.AnchoMm, v.AltoMm, v.EspesorMm) })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("VARIANTE_NO_PERTENECE_AL_ARTICULO",
                "La medida elegida no pertenece al producto A+W del artículo.");
        if (fila.Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException("VARIANTE_INACTIVA", "El producto A+W de la medida está inactivo.");
        return fila.Variante;
    }

    public async Task<IReadOnlyList<VarianteVidrioLectura>> ListarPorArticuloAsync(Guid articuloId, CancellationToken cancellationToken) =>
        await (
            from a in db.Articulos.AsNoTracking()
            join p in db.ProductosAw.AsNoTracking() on a.ProductoAwId equals p.Id
            from v in p.Variantes
            where a.Id == articuloId && p.Estatus == EstatusCatalogo.Activo
            orderby v.ClaveVariante
            select new VarianteVidrioLectura(v.Id, v.ClaveVariante, v.AnchoMm, v.AltoMm, v.EspesorMm))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, VarianteVidrioLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> varianteIds, CancellationToken cancellationToken)
    {
        if (varianteIds.Count == 0) return new Dictionary<Guid, VarianteVidrioLectura>();
        var ids = varianteIds.Distinct().ToArray();
        return await db.ProductosAw.AsNoTracking()
            .SelectMany(p => p.Variantes)
            .Where(v => ids.Contains(v.Id))
            .Select(v => new VarianteVidrioLectura(v.Id, v.ClaveVariante, v.AnchoMm, v.AltoMm, v.EspesorMm))
            .ToDictionaryAsync(v => v.VarianteId, cancellationToken);
    }
}
