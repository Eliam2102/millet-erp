using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.ListarRequisicionesDisponibles;

/// <summary>
/// Devuelve las RQs disponibles para consolidar en una OC nueva. Filtra
/// por <c>estado = EnSurtido</c>, <c>comprometida_en_oc_id IS NULL</c> y
/// al menos una línea con saldo de compra &gt; 0 (<see cref="SaldoCompraRq"/>)
/// para la sucursal solicitada. El query filter del DbContext aplica
/// empresa actual + soft delete automáticamente.
///
/// <para>
/// <b>Decisión 2026-05-13 (PR-A setting AutoGenerarOcAlAutorizar):</b> el
/// filtro cambió de <c>Autorizada</c> a <c>EnSurtido</c>. Cuando el
/// setting está <c>false</c> (default), el handler de Autorizar deja la
/// RQ en EnSurtido sin OC asociada — esas son las RQs convertibles. Si
/// el setting está <c>true</c>, la RQ pasa por EnSurtido con
/// ComprometidaEnOcId != null y queda excluida por el segundo filtro.
/// </para>
///
/// <para>
/// Una RQ cubierta 100% por almacén (sin saldo) no aparece: no hay nada que
/// comprar. Saldo y líneas se resuelven en lote (una consulta de saldos para
/// todas las RQ candidatas, sin N+1).
/// </para>
///
/// El índice parcial <c>ix_requisiciones_disponibles</c> (F4-PR1) cubre
/// este lookup.
/// </summary>
public sealed class ListarRequisicionesDisponiblesHandler
    : IRequestHandler<ListarRequisicionesDisponiblesQuery, IReadOnlyList<RequisicionDisponibleResponse>>
{
    private readonly ComprasDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ListarRequisicionesDisponiblesHandler(ComprasDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<IReadOnlyList<RequisicionDisponibleResponse>> Handle(
        ListarRequisicionesDisponiblesQuery query,
        CancellationToken cancellationToken)
    {
        var candidatas = await _db.Requisiciones
            .Where(x => query.SucursalesPermitidas == null || (query.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(x.SucursalId))
            .AsNoTracking()
            .Include(r => r.Lineas)
            .Where(r => r.SucursalId == query.SucursalId
                && r.Estado == EstadoRequisicion.EnSurtido
                && r.ComprometidaEnOcId == null)
            .OrderBy(r => r.FechaSolicitud)
            .ToListAsync(cancellationToken);

        var lineaIds = candidatas.SelectMany(r => r.Lineas).Select(l => l.Id).ToList();
        var saldos = await SaldoCompraRq.ObtenerAsync(_db, lineaIds, cancellationToken);
        var articulos = await _articulos.ObtenerPorIdsAsync(
            candidatas.SelectMany(r => r.Lineas).Select(l => l.ArticuloId).Distinct().ToList(),
            cancellationToken);

        var resultados = new List<RequisicionDisponibleResponse>(candidatas.Count);
        foreach (var r in candidatas)
        {
            var lineasConSaldo = r.Lineas
                .Where(l => saldos.GetValueOrDefault(l.Id) > 0m)
                .OrderBy(l => l.Posicion)
                .Select(l =>
                {
                    var articulo = articulos.GetValueOrDefault(l.ArticuloId);
                    return new LineaDisponibleResponse(
                        l.Id,
                        l.ArticuloId,
                        articulo?.Clave,
                        articulo?.Nombre,
                        saldos[l.Id],
                        l.CantidadDeAlmacen);
                })
                .ToList();

            if (lineasConSaldo.Count == 0)
            {
                continue;
            }

            resultados.Add(new RequisicionDisponibleResponse(
                r.Id,
                r.Folio.Valor,
                r.FolioAnio,
                r.DepartamentoId,
                r.RequisitanteId,
                r.FechaSolicitud,
                r.FechaEntregaDeseada,
                r.Lineas.Count,
                r.ProveedorSugeridoId,
                FaltantePorComprar: lineasConSaldo.Sum(l => l.CantidadSaldo),
                LineasConSaldo: lineasConSaldo));
        }

        return resultados;
    }
}
