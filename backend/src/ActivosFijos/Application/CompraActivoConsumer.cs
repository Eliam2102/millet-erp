using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Application;
public sealed record FacturaConciliadaActivo(Guid EmpresaId, Guid FacturaProveedorId, string? Moneda, decimal? TipoCambio, IReadOnlyList<LineaFacturaActivo> Lineas);
public sealed record LineaFacturaActivo(Guid LineaFacturaId, Guid? LineaOcId, decimal Cantidad, decimal Importe, decimal? ValorCapitalizable);
public sealed class CompraActivoConsumer(ActivosFijosDbContext db, IComprasOcReadPort compras)
{
    public async Task RecibirAsync(OcRecepcionRegistradaIntegrationEvent e, CancellationToken ct)
    {
        var oc = await compras.ObtenerAsync(e.OrdenCompraId, ct) ?? throw new BusinessRuleException("AF_OC_NO_EXISTE", "No existe la OC recibida.");
        if (oc.EmpresaId != e.EmpresaId) throw new BusinessRuleException("AF_EMPRESA_INVALIDA", "La OC pertenece a otra empresa.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var l in e.Lineas.Where(x => x.EsActivoFijo).GroupBy(x => x.LineaOcId).Select(g => g.First() with { Cantidad = g.Sum(x => x.Cantidad) }))
        {
            if (l.Cantidad <= 0 || l.Cantidad != decimal.Truncate(l.Cantidad) || l.LineaOcId == null || l.ClaseActivoId == null)
                throw new BusinessRuleException("AF_RECEPCION_INVALIDA", "La recepción de activos requiere clase, línea y unidades enteras.");
            var clase = await db.Clases.SingleAsync(x => x.Id == l.ClaseActivoId && x.EmpresaId == e.EmpresaId, ct);
            var llave = clase.Id.ToString();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({llave}, 0))", ct);
            var lineaLlave = l.LineaOcId.Value.ToString();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lineaLlave}, 0))", ct);
            if (await db.Activos.AnyAsync(x => x.EmpresaId == e.EmpresaId && x.RecepcionId == e.RecepcionId && x.LineaOcId == l.LineaOcId, ct)) continue;
            for (var unidad = 1; unidad <= l.Cantidad; unidad++)
            {
                var folio = await NumeroActivo.ReservarAsync(db, clase, ct);
                var a = new Activo(Guid.CreateVersion7(), e.EmpresaId, folio,
                    new DatosActivo($"Activo de {oc.Folio}", clase.Id, null, null, null, e.FechaMovimiento, null, 0, "MXN", 1,
                        new CustodiaActivo(Guid.Empty, oc.SucursalDestinoId ?? throw new BusinessRuleException("AF_SUCURSAL_REQUERIDA", "La OC requiere sucursal."), "Por completar", l.CentroCostoId ?? Guid.Empty, null), TipoImportacionActivo.No, null, false, null),
                    oc.Id, l.LineaOcId, e.RecepcionId, unidad);
                db.Activos.Add(a);
            }
            await db.SaveChangesAsync(ct);
            await AplicarFacturasAsync(e.EmpresaId, l.LineaOcId.Value, ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ConciliarAsync(FacturaConciliadaActivo e, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var l in e.Lineas.Where(x => x.LineaOcId != null))
        {
            var llave = l.LineaOcId!.Value.ToString();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({llave}, 0))", ct);
            if (!await db.Facturas.AnyAsync(x => x.EmpresaId == e.EmpresaId && x.LineaFacturaId == l.LineaFacturaId, ct))
            {
                db.Facturas.Add(new FacturaActivo { EmpresaId = e.EmpresaId, FacturaId = e.FacturaProveedorId, LineaFacturaId = l.LineaFacturaId,
                    LineaOcId = l.LineaOcId.Value, Cantidad = l.Cantidad, Importe = l.ValorCapitalizable ?? l.Importe, Moneda = e.Moneda ?? "MXN", TipoCambio = e.TipoCambio ?? 1 });
                await db.SaveChangesAsync(ct);
            }
            await AplicarFacturasAsync(e.EmpresaId, l.LineaOcId.Value, ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task AplicarFacturasAsync(Guid empresa, Guid linea, CancellationToken ct)
    {
        var activos = await db.Activos.Where(x => x.EmpresaId == empresa && x.LineaOcId == linea).OrderBy(x => x.Numero).ToListAsync(ct);
        var facturas = await db.Facturas.Where(x => x.EmpresaId == empresa && x.LineaOcId == linea).OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var f in facturas)
        {
            if (f.Cantidad <= 0 || f.Cantidad != decimal.Truncate(f.Cantidad))
            {
                if (activos.Count > 0) throw new BusinessRuleException("AF_FACTURA_CANTIDAD", "La factura de activos debe contener unidades enteras.");
                continue;
            }
            var asignadas = activos.Count(x => x.LineaFacturaId == f.LineaFacturaId);
            foreach (var a in activos.Where(x => x.FacturaProveedorId == null).Take((int)f.Cantidad - asignadas))
            {
                var unitario = decimal.Round(f.Importe / f.Cantidad, 2);
                var valor = ++asignadas == f.Cantidad ? f.Importe - unitario * (f.Cantidad - 1) : unitario;
                a.ConciliarFactura(f.FacturaId, valor, f.Moneda, f.TipoCambio, f.LineaFacturaId);
            }
        }
    }
}
