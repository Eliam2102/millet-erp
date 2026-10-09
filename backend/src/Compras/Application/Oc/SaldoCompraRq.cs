using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc;

internal static class SaldoCompraRq
{
    // Serializa consumo de saldo en Compras, incluyendo líneas de OCs distintas.
    // Se toma antes de leer y se libera al confirmar estado + outbox.
    public static async Task<IDbContextTransaction?> BloquearAsync(ComprasDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(720031005)", ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    public static decimal Calcular(decimal cantidadDeCompra, decimal comprometido, decimal recibidoCancelado) =>
        Math.Max(0, cantidadDeCompra - comprometido - recibidoCancelado);

    public static async Task<Dictionary<Guid, decimal>> ObtenerAsync(ComprasDbContext db,
        IEnumerable<Guid> lineaIds, CancellationToken ct, Guid? excluirLineaOcId = null)
    {
        var ids = lineaIds.Distinct().ToArray();
        var lineasRq = await db.LineaRequisiciones.Where(l => ids.Contains(l.Id))
            .Select(l => new { l.Id, l.CantidadDeCompra }).ToListAsync(ct);
        var consumos = await (from l in db.LineasOrdenCompra
            join oc in db.OrdenesCompra on l.OrdenCompraId equals oc.Id
            where l.LineaRequisicionId.HasValue && ids.Contains(l.LineaRequisicionId.Value)
                && l.Id != excluirLineaOcId
            select new { LineaId = l.LineaRequisicionId!.Value, oc.Estado, l.Cantidad, l.CantidadRecibida })
            .ToListAsync(ct);
        return lineasRq.ToDictionary(l => l.Id, l => Calcular(l.CantidadDeCompra,
            consumos.Where(c => c.LineaId == l.Id && c.Estado != EstadoOrdenCompra.Cancelada).Sum(c => c.Cantidad),
            consumos.Where(c => c.LineaId == l.Id && c.Estado == EstadoOrdenCompra.Cancelada).Sum(c => c.CantidadRecibida)));
    }

    public static void Validar(decimal cantidad, decimal saldo)
    {
        if (cantidad > saldo)
            throw new BusinessRuleException("OC_EXCEDE_SALDO_RQ",
                $"La cantidad supera el saldo pendiente de compra de la requisición ({saldo:0.####}).");
    }
}
