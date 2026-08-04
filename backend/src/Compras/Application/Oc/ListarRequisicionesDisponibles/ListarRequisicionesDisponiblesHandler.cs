using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.ListarRequisicionesDisponibles;

/// <summary>
/// Devuelve las RQs disponibles para consolidar en una OC nueva. Filtra
/// por <c>estado = EnSurtido</c> y <c>comprometida_en_oc_id IS NULL</c>
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
/// El índice parcial <c>ix_requisiciones_disponibles</c> (F4-PR1) cubre
/// este lookup.
/// </summary>
public sealed class ListarRequisicionesDisponiblesHandler
    : IRequestHandler<ListarRequisicionesDisponiblesQuery, IReadOnlyList<RequisicionDisponibleResponse>>
{
    private readonly ComprasDbContext _db;

    public ListarRequisicionesDisponiblesHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RequisicionDisponibleResponse>> Handle(
        ListarRequisicionesDisponiblesQuery query,
        CancellationToken cancellationToken)
    {
        var resultados = await _db.Requisiciones
            .AsNoTracking()
            .Where(r => r.SucursalId == query.SucursalId
                && r.Estado == EstadoRequisicion.EnSurtido
                && r.ComprometidaEnOcId == null)
            .OrderBy(r => r.FechaSolicitud)
            .Select(r => new RequisicionDisponibleResponse(
                r.Id,
                r.Folio.Valor,
                r.FolioAnio,
                r.DepartamentoId,
                r.RequisitanteId,
                r.FechaSolicitud,
                r.FechaEntregaDeseada,
                r.Lineas.Count,
                r.ProveedorSugeridoId))
            .ToListAsync(cancellationToken);

        return resultados;
    }
}
