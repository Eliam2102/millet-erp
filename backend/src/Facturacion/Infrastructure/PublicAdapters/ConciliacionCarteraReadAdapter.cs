using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// C7.7: comprobantes de un día contable que debieron llegar a la cartera de CxC,
/// con los mismos criterios con los que se publican sus eventos: factura de venta
/// timbrada (sin anticipos), NC con factura relacionada (o la factura que amortiza),
/// REP por factura pagada, cobro de mostrador sobre una factura de venta, y lo
/// cancelado ese día.
/// </summary>
public sealed class ConciliacionCarteraReadAdapter(FacturacionDbContext db) : IConciliacionFacturacionReadPort
{
    // Mismo día contable que EventosContablesFacturacion.FechaContable.
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Merida");

    private static readonly EstadoTimbrado[] Emitidos =
        [EstadoTimbrado.Timbrado, EstadoTimbrado.CancelacionPendiente, EstadoTimbrado.Cancelado];

    public async Task<ComprobantesDelDia> ConsultarDiaAsync(DateOnly dia, CancellationToken cancellationToken)
    {
        var inicio = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), Zona), TimeSpan.Zero);
        var fin = inicio.AddDays(1);

        // Cancelaciones aceptadas por el SAT ese día, aunque el comprobante sea de otro día.
        var canceladosHoy = await db.SolicitudesCancelacion.AsNoTracking()
            .Where(s => s.Estado == EstadoSolicitudCancelacion.Aceptada && s.ResueltaEn >= inicio && s.ResueltaEn < fin)
            .Select(s => s.ComprobanteId)
            .ToListAsync(cancellationToken);

        var facturas = await db.FacturasVenta.AsNoTracking()
            .Where(f => Emitidos.Contains(f.Estado)
                && ((f.FechaTimbrado >= inicio && f.FechaTimbrado < fin) || canceladosHoy.Contains(f.Id)))
            .Select(f => new FacturaFacturacion(f.EmpresaId, f.Id, f.Folio, f.Uuid, f.ReceptorRfc, f.ReceptorNombre,
                f.Total, f.Estado == EstadoTimbrado.Cancelado))
            .ToListAsync(cancellationToken);

        var aplicaciones = new List<AplicacionFacturacion>();

        var ncs = await db.NotasCredito.AsNoTracking()
            .Where(n => Emitidos.Contains(n.Estado)
                && ((n.FechaTimbrado >= inicio && n.FechaTimbrado < fin) || canceladosHoy.Contains(n.Id)))
            .Select(n => new { n.EmpresaId, n.Id, n.Folio, n.Uuid, n.ReceptorRfc, n.ReceptorNombre, n.Total, n.Estado, n.FacturaRelacionadaId })
            .ToListAsync(cancellationToken);
        var sinFactura = ncs.Where(n => n.FacturaRelacionadaId is null).Select(n => n.Id).ToList();
        var amortizadas = sinFactura.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Set<AnticipoVinculacion>().AsNoTracking()
                .Where(v => v.NcAmortizacionId != null && sinFactura.Contains(v.NcAmortizacionId.Value))
                .ToDictionaryAsync(v => v.NcAmortizacionId!.Value, v => v.FacturaVentaId, cancellationToken);
        foreach (var n in ncs)
        {
            // Sin factura relacionada el evento es informativo para CxC (no toca cartera).
            var factura = n.FacturaRelacionadaId ?? (amortizadas.TryGetValue(n.Id, out var f) ? f : (Guid?)null);
            if (factura is Guid facturaId)
                aplicaciones.Add(new(n.EmpresaId, TipoAplicacionFacturacion.NotaCredito, n.Id, n.Folio, n.Uuid, facturaId,
                    n.ReceptorRfc, n.ReceptorNombre, n.Total, n.Estado == EstadoTimbrado.Cancelado));
        }

        var reps = await db.RecibosPago.AsNoTracking()
            .Where(r => Emitidos.Contains(r.Estado)
                && ((r.FechaTimbrado >= inicio && r.FechaTimbrado < fin) || canceladosHoy.Contains(r.Id)))
            .Select(r => new
            {
                r.EmpresaId, r.Id, r.Folio, r.Uuid, r.ReceptorRfc, r.ReceptorNombre, r.Estado,
                Facturas = r.FacturasPagadas.Select(p => new { p.FacturaVentaId, p.ImportePagado }).ToList(),
            })
            .ToListAsync(cancellationToken);
        foreach (var r in reps)
            foreach (var p in r.Facturas)
                aplicaciones.Add(new(r.EmpresaId, TipoAplicacionFacturacion.ReciboPago, r.Id, r.Folio, r.Uuid, p.FacturaVentaId,
                    r.ReceptorRfc, r.ReceptorNombre, p.ImportePagado, r.Estado == EstadoTimbrado.Cancelado));

        // Cobros de mostrador del día, y los cancelados ese día; solo sobre facturas de venta
        // (sobre otro comprobante, como un anticipo, el evento es informativo para CxC).
        var cobros = await db.CobrosMostrador.AsNoTracking()
            .Where(c => (c.FechaCobro >= inicio && c.FechaCobro < fin)
                || (c.Estado == EstadoCobroMostrador.Cancelado && c.UpdatedAt >= inicio && c.UpdatedAt < fin))
            .Join(db.FacturasVenta.AsNoTracking(), c => c.ComprobanteId, f => f.Id,
                (c, f) => new { c.EmpresaId, c.Id, f.Folio, f.Uuid, FacturaId = f.Id, f.ReceptorRfc, f.ReceptorNombre, c.Total, c.Estado })
            .ToListAsync(cancellationToken);
        foreach (var c in cobros)
            aplicaciones.Add(new(c.EmpresaId, TipoAplicacionFacturacion.CobroMostrador, c.Id, c.Folio, c.Uuid, c.FacturaId,
                c.ReceptorRfc, c.ReceptorNombre, c.Total, c.Estado == EstadoCobroMostrador.Cancelado));

        return new ComprobantesDelDia(facturas, aplicaciones);
    }
}
