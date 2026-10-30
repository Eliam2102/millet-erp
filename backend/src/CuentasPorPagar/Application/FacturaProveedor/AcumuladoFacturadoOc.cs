using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor;

/// <summary>
/// Cantidad facturada vigente (facturas no Cancelada, excluyendo
/// <paramref name="excluirFacturaId"/>) por <c>LineaOcId</c>. Las líneas
/// sin facturas vigentes quedan en 0. Compartido por captura y cancelación.
/// </summary>
internal static class AcumuladoFacturadoOc
{
    public static async Task<Dictionary<Guid, decimal>> PrevioVigenteAsync(
        CuentasPorPagarDbContext db,
        IReadOnlyCollection<Guid> lineasOcIds,
        Guid excluirFacturaId,
        CancellationToken cancellationToken)
    {
        var previos = await db.FacturasProveedor
            .Where(f => f.Estado != EstadoPasivo.Cancelada
                && f.Id != excluirFacturaId)
            .SelectMany(f => f.Lineas)
            .Where(l => l.LineaOcId.HasValue && lineasOcIds.Contains(l.LineaOcId.Value))
            .GroupBy(l => l.LineaOcId!.Value)
            .Select(g => new { LineaOcId = g.Key, Suma = g.Sum(l => l.Cantidad) })
            .ToListAsync(cancellationToken);

        return previos.ToDictionary(p => p.LineaOcId, p => p.Suma);
    }
}
