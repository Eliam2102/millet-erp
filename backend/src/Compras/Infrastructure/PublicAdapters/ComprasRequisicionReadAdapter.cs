using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Lectura de Compras para Almacén. Null significa documento inexistente;
/// el estado se devuelve siempre para que el consumidor aplique su regla
/// (recepción/surtido o consulta histórica de una devolución).
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

        var lineas = rq.Lineas
            .Select(l => new RequisicionLineaLectura(
                LineaId: l.Id,
                ArticuloId: l.ArticuloId,
                UnidadMedida: l.UnidadMedida,
                CantidadSolicitada: l.Cantidad,
                CantidadSurtida: l.CantidadDeAlmacen,
                CentroCostoId: l.CentroCostoId,
                ProyectoId: null,
                CantidadDisponibleEntregar: l.CantidadPendienteEntregar))
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
        // estado). El folio debe resolver aunque
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
