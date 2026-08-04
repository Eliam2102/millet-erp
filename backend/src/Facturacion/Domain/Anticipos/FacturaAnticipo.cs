using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Anticipos;

/// <summary>
/// CFDI de anticipo — especialización de <see cref="Comprobante"/> tipo Ingreso
/// (§4.3 levantamiento, serie dedicada <c>FANT</c>). Documenta el cobro
/// adelantado de un pedido de maquila o de obra. Lleva un concepto único (el
/// servicio "Anticipo de clientes", clave SAT 84111506) y es <b>siempre
/// nominal</b> — un anticipo no se emite a RFC genérico (invariante 4 del §4.3
/// diseño).
///
/// <para>
/// El comprobante es inmutable una vez timbrado; el saldo amortizable vive en el
/// agregado aparte <see cref="Anticipo"/> (§3.bis.3). La relación 1:1 se cierra
/// por id: el handler genera primero el id del <see cref="Anticipo"/> y lo pasa
/// como <see cref="AnticipoId"/>; el <see cref="Anticipo"/> guarda de vuelta el
/// <c>FacturaAnticipoId</c>.
/// </para>
/// </summary>
public sealed class FacturaAnticipo : Comprobante
{
    /// <summary>Tipo de anticipo (MXP/USD); fija la moneda del saldo y el asiento.</summary>
    public TipoAnticipo TipoAnticipo { get; private set; }

    /// <summary>Pedido de origen (A+W / Manual). Null si el anticipo es de obra suelto.</summary>
    public Guid? PedidoFacturableId { get; private set; }

    /// <summary>Id del agregado <see cref="Anticipo"/> que rastrea el saldo (relación 1:1).</summary>
    public Guid AnticipoId { get; private set; }

    // ---- Concepto único del anticipo (§4.3) — el XML real lo arma F12 ----
    public string ClaveProdServSat { get; private set; } = "84111506";
    public string ClaveUnidadSat { get; private set; } = "E48"; // Unidad de servicio
    public string Descripcion { get; private set; } = "Anticipo de clientes";

    private FacturaAnticipo() { }

    private FacturaAnticipo(
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
        TipoAnticipo tipoAnticipo,
        Guid? pedidoFacturableId,
        Guid anticipoId,
        string claveProdServSat,
        string claveUnidadSat,
        string descripcion)
        : base(id, empresaId, TipoComprobante.Ingreso, folio, folioNumero, sucursalId, cajaId,
               usuarioEmisorId, receptor, emisor, metodoPago: "PUE",
               formaPago, moneda, tipoCambio, periodoAnio, periodoMes)
    {
        EstablecerCanalVenta(canalVentaId);
        if (receptor.EsGenerico)
            throw new BusinessRuleException(
                "ANTICIPO_RECEPTOR_GENERICO",
                "Una factura de anticipo siempre es nominal; no admite RFC genérico (XAXX/XEXX).");

        TipoAnticipo = tipoAnticipo;
        PedidoFacturableId = pedidoFacturableId;
        AnticipoId = anticipoId;
        ClaveProdServSat = claveProdServSat;
        ClaveUnidadSat = claveUnidadSat;
        Descripcion = descripcion;
    }

    /// <summary>
    /// Crea una factura de anticipo en <see cref="EstadoTimbrado.Borrador"/> a
    /// partir del monto del anticipo y la tasa de IVA. Calcula los totales
    /// (un único concepto) y deja el comprobante listo para timbrar.
    /// </summary>
    /// <param name="anticipoId">Id pre-generado del agregado <see cref="Anticipo"/> asociado.</param>
    /// <param name="montoBase">Importe del anticipo antes de IVA (subtotal).</param>
    /// <param name="tasaIvaTraslado">Tasa de IVA trasladado (p.ej. 0.16); null/0 = exento.</param>
    public static FacturaAnticipo CrearBorrador(
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
        TipoAnticipo tipoAnticipo,
        Guid? pedidoFacturableId,
        Guid anticipoId,
        decimal montoBase,
        decimal? tasaIvaTraslado,
        string? descripcion = null,
        short? canalVentaId = null)
    {
        if (montoBase <= 0)
            throw new BusinessRuleException("ANTICIPO_MONTO_INVALIDO", "El monto del anticipo debe ser mayor que cero.");

        var factura = new FacturaAnticipo(
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
            tipoAnticipo: tipoAnticipo,
            pedidoFacturableId: pedidoFacturableId,
            anticipoId: anticipoId,
            claveProdServSat: "84111506",
            claveUnidadSat: "E48",
            descripcion: string.IsNullOrWhiteSpace(descripcion) ? "Anticipo de clientes" : descripcion!);

        var iva = tasaIvaTraslado is > 0 ? Math.Round(montoBase * tasaIvaTraslado.Value, 2) : 0m;
        var total = montoBase + iva;
        factura.EstablecerTotales(
            subtotal: montoBase,
            descuento: 0m,
            impuestosTrasladados: iva,
            retenciones: 0m,
            total: total);

        return factura;
    }
}
