using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Lectura de Compras para Almacén. Null significa documento inexistente;
/// el estado se devuelve siempre para que el consumidor aplique su regla
/// (recepción/surtido o consulta histórica de una devolución).
/// </summary>
public sealed class ComprasOcReadAdapter : IComprasOcReadPort
{
    private readonly ComprasDbContext _db;

    public ComprasOcReadAdapter(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .AsNoTracking()
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == ocId, cancellationToken);

        if (oc is null) return null;

        var conversionMxn = ConvertirAMxn(oc.Moneda, oc.TipoCambio);

        // GAP-9 / CA2.10: los servicios no se reciben en Almacén.
        var lineas = oc.Lineas
            .Where(l => !l.EsServicio)
            .Select(l => new OcLineaLectura(
                LineaId: l.Id,
                ArticuloId: l.ArticuloId,
                UnidadMedida: l.UnidadMedida,
                CantidadSolicitada: l.Cantidad,
                CantidadRecibida: l.CantidadRecibida,
                PrecioUnitarioMxn: Math.Round(l.PrecioUnitario * conversionMxn, 4),
                CentroCostoId: l.CentroCostoId))
            .ToList();

        return new OcLectura(
            Id: oc.Id,
            Folio: oc.Folio.Valor,
            ProveedorId: oc.ProveedorId,
            EmpresaId: oc.EmpresaId,
            Estado: oc.Estado.ToString(),
            Lineas: lineas);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken cancellationToken)
    {
        if (ocIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var distinct = ocIds.Distinct().ToArray();

        // Lectura de presentación: state-agnostic a propósito (NO filtra por
        // estado). El folio debe resolver aunque
        // la OC ya esté Cerrada/Cancelada para mostrarlo en recepciones
        // históricas.
        //
        // Se proyecta el VO Folio (HasConversion ↔ columna string) y se lee
        // .Valor en memoria — acceder a .Valor dentro del árbol SQL no es
        // traducible de forma confiable sobre una propiedad value-converted
        // (lección de #396). Sin Include(Lineas): no materializa el VO Money
        // de las líneas.
        var rows = await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => distinct.Contains(o.Id))
            .Select(o => new { o.Id, o.Folio })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => x.Folio.Valor);
    }

    private static decimal ConvertirAMxn(string moneda, decimal? tipoCambio)
    {
        if (string.Equals(moneda, "MXN", StringComparison.OrdinalIgnoreCase))
        {
            return 1.0m;
        }
        // Un cambio ausente deja costo cero; RecepcionOcGuard lo rechaza explícitamente.
        return tipoCambio ?? 0m;
    }
}
