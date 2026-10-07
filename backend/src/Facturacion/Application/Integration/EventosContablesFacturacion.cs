using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;

namespace Millet.Facturacion.Application.Integration;

/// <summary>
/// Construye los eventos timbrados de Facturación con su bloque contable
/// (U1.6) desde el agregado ya timbrado. Único punto de armado: emisión,
/// reintento y pedimento publican lo mismo. Lo consume el motor contable
/// (C1.5); los consumidores actuales ignoran los campos nuevos.
/// </summary>
public static class EventosContablesFacturacion
{
    public const string ImpuestoIsr = "001";
    public const string ImpuestoIva = "002";

    // Zona por defecto de las sucursales (Sucursal.ZonaHorariaDefault).
    private static readonly TimeZoneInfo ZonaContable = TimeZoneInfo.FindSystemTimeZoneById("America/Merida");

    /// <summary>Día contable: fecha de timbrado (u ocurrencia) en hora local de Millet.</summary>
    public static DateOnly FechaContable(Comprobante c, DateTimeOffset ocurridoEn) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.FechaTimbrado ?? ocurridoEn, ZonaContable).DateTime);

    public static FacturaVentaTimbradaIntegrationEvent FacturaVentaTimbrada(
        FacturaVenta f,
        DateTimeOffset ocurridoEn,
        Guid? clienteId = null,
        IReadOnlyDictionary<Guid, string?>? tiposProducto = null) =>
        new(
            f.EmpresaId, ocurridoEn, f.Id, f.Uuid!, f.Total, f.Moneda, f.PedidoFacturableId,
            f.ReceptorRfc, f.ReceptorNombre, f.Folio, f.MetodoPago, f.FechaTimbrado,
            Subtotal: f.Subtotal,
            Descuento: f.Descuento,
            Iva: f.ImpuestosTrasladados,
            RetencionesTotal: f.Retenciones,
            Retenciones: RetencionesDe(f),
            SucursalId: f.SucursalId,
            ClienteId: clienteId,
            TipoCambio: f.TipoCambio,
            FechaContable: FechaContable(f, ocurridoEn),
            Lineas: f.Lineas
                .Select(l => new FacturaLineaContablePayload(
                    l.ProductoId,
                    l.ClaveProdServSat,
                    l.ProductoId is Guid p && tiposProducto is not null && tiposProducto.TryGetValue(p, out var tipo) ? tipo : null,
                    l.Importe,
                    l.Descuento,
                    l.ImpuestoTrasladadoImporte))
                .ToList());

    public static FacturaAnticipoTimbradaIntegrationEvent FacturaAnticipoTimbrada(
        FacturaAnticipo f, DateTimeOffset ocurridoEn, Guid? clienteId = null) =>
        new(
            f.EmpresaId, ocurridoEn, f.Id, f.AnticipoId, f.Uuid!, f.Total, f.Moneda,
            Subtotal: f.Subtotal,
            Iva: f.ImpuestosTrasladados,
            SucursalId: f.SucursalId,
            ClienteId: clienteId,
            TipoCambio: f.TipoCambio,
            FechaContable: FechaContable(f, ocurridoEn));

    public static NotaCreditoTimbradaIntegrationEvent NotaCreditoTimbrada(
        NotaCredito nc, DateTimeOffset ocurridoEn,
        Guid? facturaRelacionadaId = null, Guid? anticipoOrigenId = null, Guid? clienteId = null) =>
        new(
            nc.EmpresaId, ocurridoEn, nc.Id, nc.Motivo.ToString(), nc.Uuid!, nc.Total,
            facturaRelacionadaId ?? nc.FacturaRelacionadaId, anticipoOrigenId ?? nc.AnticipoOrigenId,
            Subtotal: nc.Subtotal,
            Iva: nc.ImpuestosTrasladados,
            SucursalId: nc.SucursalId,
            ClienteId: clienteId,
            Moneda: nc.Moneda,
            TipoCambio: nc.TipoCambio,
            FechaContable: FechaContable(nc, ocurridoEn));

    /// <summary>Tipo A+W de cada producto de la factura (sin puerto o sin producto → sin entrada).</summary>
    public static async Task<IReadOnlyDictionary<Guid, string?>> TiposProductoAsync(
        Domain.Ports.IProductosReadPort? productos, FacturaVenta f, CancellationToken cancellationToken)
    {
        var tipos = new Dictionary<Guid, string?>();
        if (productos is null)
            return tipos;
        foreach (var id in f.Lineas.Where(l => l.ProductoId is not null).Select(l => l.ProductoId!.Value).Distinct())
            tipos[id] = (await productos.ObtenerAsync(id, cancellationToken))?.Tipo;
        return tipos;
    }

    /// <summary>IVA proporcional al cobro de una factura; null si el comprobante no traslada IVA o no es factura.</summary>
    public static decimal? IvaCobrado(Comprobante comprobante, decimal importeCobrado)
    {
        if (comprobante.Tipo != TipoComprobante.Ingreso || comprobante.Total <= 0m || comprobante.ImpuestosTrasladados <= 0m)
            return null;
        var proporcion = Math.Min(1m, importeCobrado / comprobante.Total);
        return Math.Round(comprobante.ImpuestosTrasladados * proporcion, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Desglose por impuesto. El IVA retenido se recalcula por línea y el ISR
    /// es el resto, para que la suma cuadre exacto con <c>Retenciones</c> del
    /// comprobante (que redondea por línea el total combinado).
    /// </summary>
    private static List<RetencionContablePayload>? RetencionesDe(FacturaVenta f)
    {
        if (f.Retenciones <= 0m)
            return null;
        var iva = f.Lineas.Sum(l => Math.Round(
            (l.Importe - l.Descuento) * (l.TasaRetencionIva ?? 0m), 2, MidpointRounding.AwayFromZero));
        iva = Math.Min(iva, f.Retenciones);
        var isr = f.Retenciones - iva;
        var lista = new List<RetencionContablePayload>();
        if (iva > 0m) lista.Add(new(ImpuestoIva, null, iva));
        if (isr > 0m) lista.Add(new(ImpuestoIsr, null, isr));
        return lista;
    }
}
