using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.NotasCredito;

/// <summary>
/// Nota de crédito — especialización de <see cref="Comprobante"/> tipo Egreso
/// (§4.4 levantamiento). F4-PR2 cubre el motivo <see cref="MotivoNotaCredito.Amortizacion"/>
/// (aplicación de anticipo): se autogenera y timbra en la misma transacción que
/// la factura final, relacionando (07) tanto la factura de anticipo como la
/// factura final (§6.3). Bonificación / devolución entran en F5.
/// </summary>
public sealed class NotaCredito : Comprobante
{
    public MotivoNotaCredito Motivo { get; private set; }

    /// <summary>Anticipo que se amortiza (solo motivo Amortización).</summary>
    public Guid? AnticipoOrigenId { get; private set; }

    /// <summary>Factura final / origen relacionada.</summary>
    public Guid? FacturaRelacionadaId { get; private set; }

    /// <summary>Importe que afecta inventario: 0 en amortización y bonificación, ≠0 en devolución.</summary>
    public decimal ImporteAfectaInventario { get; private set; }

    // ---- Concepto único (el XML real lo arma F12) ----
    public string ClaveProdServSat { get; private set; } = "84111506";
    public string ClaveUnidadSat { get; private set; } = "E48";
    public string Descripcion { get; private set; } = "Aplicación de anticipo";

    private NotaCredito() { }

    private NotaCredito(
        Guid id,
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        MotivoNotaCredito motivo,
        Guid? anticipoOrigenId,
        Guid? facturaRelacionadaId,
        string claveProdServSat,
        string claveUnidadSat,
        string descripcion)
        : base(id, empresaId, TipoComprobante.Egreso, folio, folioNumero, sucursalId, cajaId,
               usuarioEmisorId, receptor, emisor, metodoPago: "PUE",
               formaPago, moneda, tipoCambio, periodoAnio, periodoMes)
    {
        // La NC hereda dimensiones (sucursal/canal) de la factura relacionada
        // — la de amortización, de la factura final ([Decisión 12-10]).
        EstablecerCanalVenta(canalVentaId);
        Motivo = motivo;
        AnticipoOrigenId = anticipoOrigenId;
        FacturaRelacionadaId = facturaRelacionadaId;
        ImporteAfectaInventario = 0m;
        ClaveProdServSat = claveProdServSat;
        ClaveUnidadSat = claveUnidadSat;
        Descripcion = descripcion;
    }

    /// <summary>
    /// Crea la NC de amortización de un anticipo en <see cref="EstadoTimbrado.Borrador"/>.
    /// <paramref name="montoTotal"/> es el importe a amortizar (con IVA); se
    /// desglosa con <paramref name="tasaIva"/> para los totales. El handler agrega
    /// las relaciones 07 (factura de anticipo + factura final) antes de timbrar.
    /// </summary>
    public static NotaCredito CrearAmortizacion(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        Guid anticipoOrigenId,
        Guid facturaRelacionadaId,
        decimal montoTotal,
        decimal? tasaIva)
    {
        if (montoTotal <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto de la nota de crédito debe ser mayor que cero.");

        var nc = new NotaCredito(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: sucursalId,
            cajaId: cajaId,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            formaPago: formaPago,
            moneda: moneda,
            tipoCambio: tipoCambio,
            periodoAnio: periodoAnio,
            periodoMes: periodoMes,
            canalVentaId: canalVentaId,
            motivo: MotivoNotaCredito.Amortizacion,
            anticipoOrigenId: anticipoOrigenId,
            facturaRelacionadaId: facturaRelacionadaId,
            claveProdServSat: "84111506",
            claveUnidadSat: "E48",
            descripcion: "Aplicación de anticipo");

        DesglosarTotales(nc, montoTotal, tasaIva);
        return nc;
    }

    /// <summary>
    /// Crea la NC por bonificación de una factura de venta en
    /// <see cref="EstadoTimbrado.Borrador"/> (§7.1 levantamiento: el descuento
    /// comercial va por NC posterior, <b>nunca</b> en el XML de la venta).
    /// Relación 01 a la factura origen (la agrega el handler). El handler valida
    /// que la factura origen no esté cancelada (invariante 6).
    /// </summary>
    public static NotaCredito CrearBonificacion(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        Guid facturaRelacionadaId,
        decimal montoTotal,
        decimal? tasaIva,
        string? descripcion)
    {
        if (montoTotal <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto de la nota de crédito debe ser mayor que cero.");

        var nc = new NotaCredito(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: sucursalId,
            cajaId: cajaId,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            formaPago: formaPago,
            moneda: moneda,
            tipoCambio: tipoCambio,
            periodoAnio: periodoAnio,
            periodoMes: periodoMes,
            canalVentaId: canalVentaId,
            motivo: MotivoNotaCredito.Bonificacion,
            anticipoOrigenId: null,
            facturaRelacionadaId: facturaRelacionadaId,
            claveProdServSat: "84111506",
            claveUnidadSat: "E48",
            descripcion: string.IsNullOrWhiteSpace(descripcion) ? "Bonificación" : descripcion!);

        DesglosarTotales(nc, montoTotal, tasaIva);
        return nc;
    }

    /// <summary>
    /// Crea la NC por la "ranura" del pedido A+W en
    /// <see cref="EstadoTimbrado.Borrador"/> (RANURA-PR2). La factura de la
    /// venta va por el total y esta NC documenta el descuento de cabecera;
    /// se autogenera y timbra en la misma transacción que la factura
    /// (patrón de la amortización). Relación 01 a la factura origen (la
    /// agrega el emisor). <paramref name="montoTotal"/> es la ranura BRUTA
    /// (con IVA), tal como la entrega A+W.
    /// </summary>
    public static NotaCredito CrearRanura(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        Guid facturaRelacionadaId,
        decimal montoTotal,
        decimal? tasaIva,
        string descripcion)
    {
        if (montoTotal <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto de la nota de crédito debe ser mayor que cero.");

        var nc = new NotaCredito(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: sucursalId,
            cajaId: cajaId,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            formaPago: formaPago,
            moneda: moneda,
            tipoCambio: tipoCambio,
            periodoAnio: periodoAnio,
            periodoMes: periodoMes,
            canalVentaId: canalVentaId,
            motivo: MotivoNotaCredito.Ranura,
            anticipoOrigenId: null,
            facturaRelacionadaId: facturaRelacionadaId,
            claveProdServSat: "84111506",
            claveUnidadSat: "E48",
            descripcion: descripcion);

        DesglosarTotales(nc, montoTotal, tasaIva);
        return nc;
    }

    /// <summary>Desglosa el total en subtotal + IVA (el detalle fiscal exacto lo afina F12).</summary>
    private static void DesglosarTotales(NotaCredito nc, decimal montoTotal, decimal? tasaIva)
    {
        var subtotal = tasaIva is > 0 ? Math.Round(montoTotal / (1 + tasaIva.Value), 2) : montoTotal;
        var iva = montoTotal - subtotal;
        nc.EstablecerTotales(subtotal, descuento: 0m, impuestosTrasladados: iva, retenciones: 0m, total: montoTotal);
    }
}
