using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IAlmacenEntregasReadPort"/>. Lee las
/// líneas de movimientos tipo <c>SalidaConsumo</c> en estado
/// <c>Registrado</c> con <c>RqId</c> en el conjunto pedido y las parte en
/// dos proyecciones por RQ: lo que trae <c>LineaRqId</c> (atribución exacta
/// por línea) y lo que no (atribuible solo por <c>ArticuloId</c>).
///
/// <para>
/// Bypass de empresa: los movimientos no se filtran por la empresa actual
/// (el job de migración corre con bypass de empresa, igual que los demás
/// jobs de reconciliación).
/// </para>
/// </summary>
public sealed class AlmacenEntregasReadAdapter : IAlmacenEntregasReadPort
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public AlmacenEntregasReadAdapter(
        AlmacenDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyDictionary<Guid, EntregaReclasificacion>> ObtenerEntregaParaReclasificacionAsync(
        IReadOnlyCollection<Guid> requisicionIds,
        CancellationToken cancellationToken)
    {
        if (requisicionIds.Count == 0)
        {
            return new Dictionary<Guid, EntregaReclasificacion>();
        }

        using var bypass = _empresaContext.Bypass();
        var ids = requisicionIds.Distinct().ToArray();

        // Una pasada por las líneas de salida (SalidaConsumo Registrado) de
        // las RQ pedidas, cargando el rq_id de la cabecera junto a cada línea.
        // Se agrupa/parte en memoria (el conjunto candidato es chico) por
        // LineaRqId nulo/no-nulo, evitando GroupBy traducido frágil.
        var filas = await _db.Movimientos
            .AsNoTracking()
            .Where(m => m.Tipo == TipoMovimiento.SalidaConsumo
                     && m.Estado == EstadoMovimiento.Registrado
                     && m.RqId != null
                     && ids.Contains(m.RqId.Value))
            .SelectMany(m => m.Lineas, (m, l) => new
            {
                RqId = m.RqId!.Value,
                l.ArticuloId,
                l.LineaRqId,
                l.Cantidad,
            })
            .ToListAsync(cancellationToken);

        var resultado = new Dictionary<Guid, EntregaReclasificacion>();
        foreach (var grupo in filas.GroupBy(f => f.RqId))
        {
            var porLinea = grupo
                .Where(f => f.LineaRqId != null)
                .GroupBy(f => f.LineaRqId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(f => f.Cantidad));

            var porArticuloSinLinea = grupo
                .Where(f => f.LineaRqId == null)
                .GroupBy(f => f.ArticuloId)
                .ToDictionary(g => g.Key, g => g.Sum(f => f.Cantidad));

            resultado[grupo.Key] = new EntregaReclasificacion(porLinea, porArticuloSinLinea);
        }

        return resultado;
    }
}
