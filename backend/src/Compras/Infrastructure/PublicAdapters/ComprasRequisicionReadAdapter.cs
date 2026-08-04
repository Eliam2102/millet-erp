using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IComprasRequisicionReadPort"/>
/// declarado en <c>Almacen.Domain.Ports</c>. Reemplaza el
/// <c>NoOpComprasRequisicionReadPort</c> de Almacén
/// (PLATFORM-TODO &lt;ComprasRqReadAdapter&gt;).
///
/// <para>Lectura cross-módulo via <see cref="ComprasDbContext"/> con
/// <c>AsNoTracking</c>. Filtra por estados que aceptan surtido
/// (<see cref="EstadoRequisicion.Autorizada"/>,
/// <see cref="EstadoRequisicion.EnSurtido"/>). El estado "Surtida"
/// se deriva del cubrimiento — el handler de Almacén pasa la RQ pero
/// rechaza si la línea ya está cubierta.</para>
///
/// <para><b>Mapeo línea</b>:</para>
/// <list>
///   <item><c>CantidadSurtida</c> = <see cref="LineaRequisicion.CantidadDeAlmacen"/>:
///     lo que el almacén ya entregó. (No incluye CantidadDeCompra, que se
///     surte vía OC y entra como recepción, no como salida.)</item>
///   <item><c>ProyectoId</c> = <c>null</c>: el dominio actual de LineaRequisicion
///     no tracquea proyecto por línea (solo centroCostoId). Cuando se
///     introduzca, este mapping se actualizará.</item>
/// </list>
/// </summary>
public sealed class ComprasRequisicionReadAdapter : IComprasRequisicionReadPort
{
    private readonly ComprasDbContext _db;

    public ComprasRequisicionReadAdapter(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken cancellationToken)
    {
        var rq = await _db.Requisiciones
            .AsNoTracking()
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == rqId, cancellationToken);

        if (rq is null) return null;

        // Solo aceptamos RQs autorizadas o en surtido. Borrador /
        // EnAutorizacion / Cerrada / Cancelada / Rechazada / Eliminada
        // no aceptan salidas.
        if (rq.Estado != EstadoRequisicion.Autorizada
            && rq.Estado != EstadoRequisicion.EnSurtido)
        {
            return null;
        }

        var lineas = rq.Lineas
            .Select(l => new RequisicionLineaLectura(
                LineaId: l.Id,
                ArticuloId: l.ArticuloId,
                UnidadMedida: l.UnidadMedida,
                CantidadSolicitada: l.Cantidad,
                CantidadSurtida: l.CantidadDeAlmacen,
                CentroCostoId: l.CentroCostoId,
                ProyectoId: null))
            .ToList();

        return new RequisicionLectura(
            Id: rq.Id,
            Folio: rq.Folio.Valor,
            EmpresaId: rq.EmpresaId,
            DepartamentoId: rq.DepartamentoId,
            AlmacenDestinoId: rq.AlmacenDestinoId,
            PersonaDestinatariaId: rq.RequisitanteId,
            Estado: rq.Estado.ToString(),
            Lineas: lineas);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
        IReadOnlyCollection<Guid> rqIds,
        CancellationToken cancellationToken)
    {
        if (rqIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var distinct = rqIds.Distinct().ToArray();

        // Lectura de presentación: state-agnostic a propósito (NO filtra por
        // estado, a diferencia de ObtenerAsync). El folio debe resolver aunque
        // la RQ ya esté Surtida/Cerrada para mostrarlo en salidas históricas.
        //
        // Se proyecta el VO Folio (HasConversion ↔ columna string) y se lee
        // .Valor en memoria — acceder a .Valor dentro del árbol SQL no es
        // traducible de forma confiable sobre una propiedad value-converted.
        var rows = await _db.Requisiciones
            .AsNoTracking()
            .Where(r => distinct.Contains(r.Id))
            .Select(r => new { r.Id, r.Folio })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => x.Folio.Valor);
    }
}
