using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
public sealed class CapitalizacionProveedorReadAdapter(CuentasPorPagarDbContext db) : ICapitalizacionProveedorReadPort
{
    public async Task<IReadOnlyList<DocumentoCapitalizacionOpcion>> BuscarAsync(bool notaCredito, Guid? facturaId, Guid sucursalId, string? q, CancellationToken ct)
    {
        if (notaCredito && facturaId is null) return [];
        if (notaCredito)
            return await db.NotasCreditoProveedor.Where(x => x.FacturaOrigenId == facturaId && x.Estado != EstadoNotaCredito.Cancelada)
                .OrderByDescending(x => x.CreatedAt).Take(100)
                .Select(x => new DocumentoCapitalizacionOpcion(x.Id, x.FacturaOrigenId!.Value, x.FolioProveedor ?? "Nota de crédito", x.Subtotal * (x.TipoCambio ?? 1))).ToListAsync(ct);
        var facturas = db.FacturasProveedor.Where(x => x.SucursalId == sucursalId && x.Estado != EstadoPasivo.Cancelada);
        if (!string.IsNullOrWhiteSpace(q)) facturas = facturas.Where(x => x.FolioProveedor != null && x.FolioProveedor.Contains(q));
        return await facturas.OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new DocumentoCapitalizacionOpcion(x.Id, x.Id, (x.SerieProveedor ?? "") + " " + (x.FolioProveedor ?? "Factura"), (x.Subtotal - x.Descuentos) * (x.TipoCambio ?? 1))).ToListAsync(ct);
    }
    public async Task<DocumentoCapitalizacion?> ObtenerAsync(Guid documentoId, Guid facturaId, bool notaCredito, CancellationToken ct)
    {
        if (!await db.FacturasProveedor.AnyAsync(x => x.Id == facturaId && x.Estado != EstadoPasivo.Cancelada, ct)) return null;
        if (notaCredito)
            return await db.NotasCreditoProveedor.Where(x => x.Id == documentoId && x.FacturaOrigenId == facturaId && x.Estado != EstadoNotaCredito.Cancelada)
                .Select(x => new DocumentoCapitalizacion(x.Id, facturaId, x.Subtotal * (x.TipoCambio ?? 1))).SingleOrDefaultAsync(ct);
        return documentoId == facturaId ? await db.FacturasProveedor.Where(x => x.Id == facturaId)
            .Select(x => new DocumentoCapitalizacion(x.Id, x.Id, (x.Subtotal - x.Descuentos) * (x.TipoCambio ?? 1))).SingleOrDefaultAsync(ct) : null;
    }
}
