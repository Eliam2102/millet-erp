using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Facturas;

/// <summary>
/// Factura de venta — especialización de <see cref="Comprobante"/> tipo Ingreso
/// (§4.2 diseño). Modela los dos ejes ortogonales (<see cref="CanalVentaId"/> ×
/// <see cref="ComportamientoFiscal"/>), el número de Obra (snapshot, no va al
/// XML) y las líneas de venta.
///
/// <para>
/// F1-PR1: factura nominal o a RFC genérico, emitida desde captura directa
/// (sin <c>PedidoFacturable</c> todavía — ese agregado entra en F1-PR2). El
/// flujo de emisión la lleva de <c>Borrador</c> a <c>Timbrado</c> contra el
/// stub de timbrado.
/// </para>
/// </summary>
public sealed class FacturaVenta : Comprobante
{
    public ComportamientoFiscal ComportamientoFiscal { get; private set; }

    /// <summary>Pedido de origen (A+W / Planta Pintura / Manual). Null en captura directa F1-PR1.</summary>
    public Guid? PedidoFacturableId { get; private set; }

    // ---- Obra (snapshot: id numérico + nombre texto; NO aparece en el XML, §7.4) ----
    public long? ObraId { get; private set; }
    public string? ObraNombre { get; private set; }

    /// <summary>True si consolida múltiples pedidos del mismo cliente.</summary>
    public bool FacturaAgrupada { get; private set; }

    /// <summary>Autorización previa (venta de activo fijo, §7.8). Null por default.</summary>
    public Guid? AutorizacionId { get; private set; }

    private readonly List<FacturaVentaLinea> _lineas = [];
    public IReadOnlyCollection<FacturaVentaLinea> Lineas => _lineas.AsReadOnly();

    /// <summary>Complemento de Comercio Exterior (1:1 opcional), presente solo en exportación con CCE (F7-PR1).</summary>
    public Cce.ComplementoCce? ComplementoCce { get; private set; }

    private FacturaVenta() { }

    private FacturaVenta(
        Guid id,
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string metodoPago,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short canalVentaId,
        ComportamientoFiscal comportamientoFiscal,
        Guid? pedidoFacturableId,
        long? obraId,
        string? obraNombre,
        bool facturaAgrupada)
        : base(id, empresaId, TipoComprobante.Ingreso, folio, folioNumero, sucursalId, cajaId,
               usuarioEmisorId, receptor, emisor, metodoPago, formaPago,
               moneda, tipoCambio, periodoAnio, periodoMes)
    {
        if (canalVentaId <= 0)
            throw new BusinessRuleException("FACTURA_CANAL_INVALIDO", "El canal de venta es obligatorio en una factura de venta.");
        EstablecerCanalVenta(canalVentaId);
        ComportamientoFiscal = comportamientoFiscal;
        PedidoFacturableId = pedidoFacturableId;
        ObraId = obraId;
        ObraNombre = obraNombre;
        FacturaAgrupada = facturaAgrupada;
    }

    /// <summary>
    /// Crea una factura de venta en <see cref="EstadoTimbrado.Borrador"/>. El
    /// handler agrega las líneas (<see cref="AgregarLinea"/>), recalcula totales
    /// (<see cref="RecalcularTotales"/>) y la lleva al timbrado.
    /// </summary>
    public static FacturaVenta CrearBorrador(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string metodoPago,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short canalVentaId,
        ComportamientoFiscal comportamientoFiscal,
        Guid? pedidoFacturableId,
        long? obraId,
        string? obraNombre,
        bool facturaAgrupada)
    {
        return new FacturaVenta(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: sucursalId,
            cajaId: cajaId,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            metodoPago: metodoPago,
            formaPago: formaPago,
            moneda: moneda,
            tipoCambio: tipoCambio,
            periodoAnio: periodoAnio,
            periodoMes: periodoMes,
            canalVentaId: canalVentaId,
            comportamientoFiscal: comportamientoFiscal,
            pedidoFacturableId: pedidoFacturableId,
            obraId: obraId,
            obraNombre: obraNombre,
            facturaAgrupada: facturaAgrupada);
    }

    /// <summary>
    /// Agrega una línea. Solo permitido mientras la factura está en
    /// <see cref="EstadoTimbrado.Borrador"/> (un CFDI timbrado es inmutable).
    /// </summary>
    public FacturaVentaLinea AgregarLinea(
        Guid? productoId,
        string claveProdServSat,
        string descripcion,
        string claveUnidadSat,
        decimal cantidad,
        decimal valorUnitario,
        decimal descuento,
        string objetoImp,
        decimal? tasaIvaTraslado,
        decimal? tasaRetencionIva,
        decimal? tasaRetencionIsr,
        bool requierePedimento = false)
    {
        if (Estado != EstadoTimbrado.Borrador)
            throw new BusinessRuleException(
                "FACTURA_INMUTABLE",
                $"No se pueden agregar líneas a una factura en estado {Estado}.");

        var linea = new FacturaVentaLinea(
            id: Guid.CreateVersion7(),
            facturaVentaId: Id,
            posicion: _lineas.Count + 1,
            productoId: productoId,
            claveProdServSat: claveProdServSat,
            descripcion: descripcion,
            claveUnidadSat: claveUnidadSat,
            cantidad: cantidad,
            valorUnitario: valorUnitario,
            descuento: descuento,
            objetoImp: objetoImp,
            tasaIvaTraslado: tasaIvaTraslado,
            tasaRetencionIva: tasaRetencionIva,
            tasaRetencionIsr: tasaRetencionIsr,
            requierePedimento: requierePedimento);

        _lineas.Add(linea);
        return linea;
    }

    /// <summary>True si alguna línea requiere pedimento — la compuerta es por factura (§3.bis.5).</summary>
    public bool RequierePedimento => _lineas.Any(l => l.RequierePedimento);

    /// <summary>True si todas las líneas que requieren pedimento ya lo tienen.</summary>
    public bool PedimentoCompleto => _lineas.Where(l => l.RequierePedimento).All(l => !string.IsNullOrWhiteSpace(l.Pedimento));

    /// <summary>
    /// Transición <c>Borrador → PendientePedimento</c>: la factura requiere
    /// pedimento y aún no está disponible; se retiene el timbre (solo esta
    /// factura, §3.bis.5).
    /// </summary>
    public void MarcarPendientePedimento()
    {
        if (!RequierePedimento)
            throw new BusinessRuleException("FACTURA_NO_REQUIERE_PEDIMENTO", "La factura no tiene líneas que requieran pedimento.");

        PasarAPendientePedimento();
    }

    /// <summary>
    /// Aplica el pedimento a las líneas que lo requieren y devuelve la factura a
    /// <c>Borrador</c>, lista para timbrar (§3.bis.5). El mismo pedimento aplica a
    /// todas las líneas pendientes (caso típico de una Hoja de Salida).
    /// </summary>
    public void AplicarPedimento(string pedimento, DateOnly? fechaDocAduanero, string? identificacionMercancia)
    {
        if (Estado != EstadoTimbrado.PendientePedimento)
            throw new BusinessRuleException("FACTURA_NO_PENDIENTE_PEDIMENTO", $"Solo una factura en PendientePedimento admite pedimento (actual: {Estado}).");

        foreach (var linea in _lineas.Where(l => l.RequierePedimento))
            linea.AplicarPedimento(pedimento, fechaDocAduanero, identificacionMercancia);

        VolverABorradorDesdePedimento();
    }

    /// <summary>
    /// Vincula la autorización del Contador General (venta de activo fijo, §7.8,
    /// F9). Solo en comportamiento <see cref="ComportamientoFiscal.VentaActivoFijo"/>
    /// y en Borrador (antes de timbrar).
    /// </summary>
    public void VincularAutorizacion(Guid autorizacionId)
    {
        if (ComportamientoFiscal != ComportamientoFiscal.VentaActivoFijo)
            throw new BusinessRuleException(
                "AUTORIZACION_COMPORTAMIENTO_INVALIDO",
                "Solo una factura de Venta de activo fijo admite autorización del Contador General.");
        if (Estado != EstadoTimbrado.Borrador)
            throw new BusinessRuleException("FACTURA_INMUTABLE", $"No se puede vincular autorización a una factura en estado {Estado}.");

        AutorizacionId = autorizacionId;
    }

    /// <summary>
    /// Adjunta el Complemento de Comercio Exterior (F7-PR1). Solo permitido en
    /// facturas con comportamiento <see cref="ComportamientoFiscal.ExportacionConCce"/>
    /// y mientras la factura está en <see cref="EstadoTimbrado.Borrador"/> (el CCE
    /// va en el XML, inmutable tras timbrar).
    /// </summary>
    public void AdjuntarCce(Cce.ComplementoCce cce)
    {
        ArgumentNullException.ThrowIfNull(cce);
        if (ComportamientoFiscal != ComportamientoFiscal.ExportacionConCce)
            throw new BusinessRuleException(
                "CCE_COMPORTAMIENTO_INVALIDO",
                "Solo una factura de Exportación con CCE admite Complemento de Comercio Exterior.");
        if (Estado != EstadoTimbrado.Borrador)
            throw new BusinessRuleException("FACTURA_INMUTABLE", $"No se puede adjuntar CCE a una factura en estado {Estado}.");

        ComplementoCce = cce;
    }

    /// <summary>
    /// Recalcula los totales del comprobante sumando las líneas. Debe invocarse
    /// tras agregar todas las líneas y antes de timbrar.
    /// <code>Total = Subtotal − Descuento + ImpuestosTrasladados − Retenciones</code>
    /// </summary>
    public void RecalcularTotales()
    {
        if (_lineas.Count == 0)
            throw new BusinessRuleException("FACTURA_SIN_LINEAS", "La factura debe tener al menos una línea.");

        var subtotal = _lineas.Sum(l => l.Importe);
        var descuento = _lineas.Sum(l => l.Descuento);
        var impuestos = _lineas.Sum(l => l.ImpuestoTrasladadoImporte);
        var retenciones = _lineas.Sum(l => l.RetencionTotalImporte);
        var total = subtotal - descuento + impuestos - retenciones;

        EstablecerTotales(subtotal, descuento, impuestos, retenciones, total);
    }
}
