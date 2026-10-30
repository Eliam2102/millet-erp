using System.Text.Json;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// C1.5 · tipos #1 y #3–#15 del plano C1 §4 (ficha 86e3kdw33 §3). Cada método lee el contrato JSON del emisor (espejo con solo los
/// campos que usa Contabilidad) y arma el <see cref="EventoContable"/> con importes con nombre que consume la plantilla sembrada.
/// Documento origen: el mismo id que traen las cancelaciones, para anular por documento (K10.3). Los comprobantes de Facturación
/// (factura de venta, de anticipo y NC) comparten el tipo <see cref="DocComprobante"/> porque <c>comprobante.cancelado.v1</c> solo trae
/// el id del comprobante.
/// </summary>
public static partial class NormalizadorEventos
{
    // ---- Wire types (código de develop; ficha C1.5 §3) ----
    public const string RecepcionOcRegistrada = "almacen.oc_recepcion.registrada.v1";
    public const string RecepcionOcCancelada = "almacen.recepcion_oc.cancelada.v1";
    public const string AjusteInventarioAplicado = "almacen.ajuste_inventario.aplicado.v1";
    public const string SalidaRequisicionRegistrada = "almacen.salida_requisicion.registrada.v1";
    public const string DevolucionInternaAplicada = "almacen.devolucion_interna.aplicada.v1";
    public const string DevolucionProveedorRegistrada = "almacen.oc_devolucion.registrada.v1";
    public const string FacturaProveedorCancelada = "cuentas_por_pagar.factura.cancelada.v1";
    public const string NotaCreditoProveedorRegistrada = "cuentas_por_pagar.nota-credito.registrada.v1";
    public const string NotaCargoAutorizada = "cuentas_por_pagar.nota-cargo.autorizada.v1";
    public const string AnticipoProveedorCapturado = "cuentas_por_pagar.anticipo.capturado.v1";
    public const string PagoProveedorAplicado = "tesoreria.pago-factura-proveedor.aplicado.v1";
    public const string PagoProveedorRevertido = "tesoreria.pago-factura-proveedor.revertido.v1";
    public const string PagoClienteConfirmado = "tesoreria.pago-cliente.confirmado.v1";
    public const string FacturaVentaTimbrada = "facturacion.factura-venta.timbrada.v1";
    public const string FacturaAnticipoTimbrada = "facturacion.factura-anticipo.timbrada.v1";
    public const string NotaCreditoTimbrada = "facturacion.nota-credito.timbrada.v1";
    public const string ComprobanteCancelado = "facturacion.comprobante.cancelado.v1";
    public const string CobroMostradorRegistrado = "facturacion.cobro-mostrador.registrado.v1";
    public const string CobroMostradorCancelado = "facturacion.cobro-mostrador.cancelado.v1";
    public const string CajaSesionCerrada = "facturacion.caja-sesion.cerrada.v1";

    // ---- Documento origen (módulo, tipo) ----
    public static readonly (string Modulo, string Tipo) DocRecepcion = ("almacen", "recepcion_oc");
    public static readonly (string Modulo, string Tipo) DocAjuste = ("almacen", "ajuste_inventario");
    public static readonly (string Modulo, string Tipo) DocSalida = ("almacen", "salida_requisicion");
    public static readonly (string Modulo, string Tipo) DocDevolucionInterna = ("almacen", "devolucion_interna");
    public static readonly (string Modulo, string Tipo) DocDevolucionProveedor = ("almacen", "devolucion_proveedor");
    public static readonly (string Modulo, string Tipo) DocFacturaProveedor = ("cuentas_por_pagar", "factura_proveedor");
    public static readonly (string Modulo, string Tipo) DocNotaCreditoProveedor = ("cuentas_por_pagar", "nota_credito_proveedor");
    public static readonly (string Modulo, string Tipo) DocNotaCargo = ("cuentas_por_pagar", "nota_cargo");
    public static readonly (string Modulo, string Tipo) DocAnticipoProveedor = ("cuentas_por_pagar", "anticipo_proveedor");
    public static readonly (string Modulo, string Tipo) DocPagoProveedor = ("tesoreria", "pago_factura_proveedor");
    public static readonly (string Modulo, string Tipo) DocPagoCliente = ("tesoreria", "pago_cliente");
    public static readonly (string Modulo, string Tipo) DocComprobante = ("facturacion", "comprobante");
    public static readonly (string Modulo, string Tipo) DocCobro = ("facturacion", "cobro_mostrador");
    public static readonly (string Modulo, string Tipo) DocCajaSesion = ("facturacion", "caja_sesion");

    // ---- Formas de pago SAT (c_FormaPago) del cobro de mostrador; reglas de cobro del 23-oct ----
    public const string FormaEfectivo = "01";
    private const string FormaCheque = "02";
    private const string FormaTransferencia = "03";
    private const string FormaTarjetaCredito = "04";
    private const string FormaTarjetaDebito = "28";

    /// <summary>Tipos que se contabilizan anulando la póliza de su documento (K10.3), no con plantilla.</summary>
    public static (string Modulo, string Tipo, Guid DocumentoId, string Motivo)? Anulacion(string tipo, string payload)
    {
        switch (tipo)
        {
            case RecepcionOcCancelada:
            {
                var c = Leer<RecepcionCanceladaContrato>(payload);
                return (DocRecepcion.Modulo, DocRecepcion.Tipo, c.RecepcionId, $"Recepción cancelada: {c.MotivoTexto}".Trim());
            }
            case FacturaProveedorCancelada:
            {
                var c = Leer<FacturaProveedorCanceladaContrato>(payload);
                return (DocFacturaProveedor.Modulo, DocFacturaProveedor.Tipo, c.FacturaProveedorId, $"Factura de proveedor cancelada: {c.MotivoTexto ?? c.Motivo}".Trim());
            }
            case PagoProveedorRevertido:
            {
                var c = Leer<PagoRevertidoContrato>(payload);
                return (DocPagoProveedor.Modulo, DocPagoProveedor.Tipo, c.PagoOriginalId, $"Pago revertido: {c.Motivo}".Trim());
            }
            case ComprobanteCancelado:
            {
                var c = Leer<ComprobanteCanceladoContrato>(payload);
                return (DocComprobante.Modulo, DocComprobante.Tipo, c.ComprobanteId, "Comprobante cancelado ante el SAT");
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Sin importes ni líneas que muevan dinero (p. ej. una recepción con solo activos fijos, C1.5-h): no hay póliza que registrar y no
    /// es rechazo. Un evento sin importes (el genérico, sin normalizador propio) no entra aquí: va al motor y queda con SinPlantilla.
    /// </summary>
    public static bool SinMovimiento(EventoContable e) =>
        e.Importes.Count > 0 && e.Importes.Values.All(v => v is null or 0m) && e.Lineas.All(l => l.Importe == 0m);

    private static EventoContable? NormalizarC15(string tipo, Guid eventoId, string payload) => tipo switch
    {
        RecepcionOcRegistrada => Recepcion(eventoId, payload),
        DevolucionProveedorRegistrada => DevolucionProveedor(eventoId, payload),
        AjusteInventarioAplicado => Ajuste(eventoId, payload),
        SalidaRequisicionRegistrada => Salida(eventoId, payload),
        NotaCreditoProveedorRegistrada => NotaProveedor(tipo, eventoId, payload, DocNotaCreditoProveedor, TipoDocumentoPuente.NotaCredito),
        NotaCargoAutorizada => NotaProveedor(tipo, eventoId, payload, DocNotaCargo, TipoDocumentoPuente.NotaCargo),
        AnticipoProveedorCapturado => AnticipoProveedor(eventoId, payload),
        PagoProveedorAplicado => PagoProveedor(eventoId, payload),
        PagoClienteConfirmado => PagoCliente(eventoId, payload),
        FacturaVentaTimbrada => FacturaVenta(eventoId, payload),
        FacturaAnticipoTimbrada => FacturaAnticipo(eventoId, payload),
        NotaCreditoTimbrada => NotaCreditoVenta(eventoId, payload),
        CobroMostradorRegistrado => CobroNoEfectivo(eventoId, payload),
        _ => null,
    };

    // ---------------------------------------------------------------- Almacén

    /// <summary>#1 EM: cargo INVENTARIO · abono puente por línea de OC. Las líneas de activo fijo no entran (C1.5-h).</summary>
    private static EventoContable Recepcion(Guid eventoId, string payload)
    {
        var r = Leer<RecepcionRegistradaContrato>(payload);
        var lineas = (r.Lineas ?? []).Where(l => !l.EsActivoFijo)
            .Select(l => new LineaEventoContable(l.LineaOcId, l.MontoTotalMxn, l.CentroCostoId, false)).ToList();
        return Mxn(RecepcionOcRegistrada, eventoId, r.EmpresaId, r.FechaMovimiento,
            Doc(DocRecepcion, r.RecepcionId, r.FolioRecepcion), r.SucursalId, ordenCompraId: r.OrdenCompraId,
            importes: new() { ["Subtotal"] = lineas.Sum(l => l.Importe) }, lineas: lineas, payload: payload,
            tipoPuente: TipoDocumentoPuente.Recepcion);
    }

    /// <summary>#15b EM: devolución a proveedor; cargo puente por línea de OC · abono INVENTARIO (la nota de cargo #3c salda el puente).</summary>
    private static EventoContable DevolucionProveedor(Guid eventoId, string payload)
    {
        var d = Leer<DevolucionProveedorContrato>(payload);
        var lineas = (d.Lineas ?? []).Select(l => new LineaEventoContable(l.LineaOcId, l.MontoTotalMxn, null, false)).ToList();
        return Mxn(DevolucionProveedorRegistrada, eventoId, d.EmpresaId, FechaContable.HoyLocal(d.OcurridoEn),
            Doc(DocDevolucionProveedor, d.DevolucionId, d.FolioMovimiento), d.SucursalId, proveedorId: d.ProveedorId,
            ordenCompraId: d.OrdenCompraOrigenId, importes: new() { ["Subtotal"] = lineas.Sum(l => l.Importe) }, lineas: lineas,
            payload: payload, tipoPuente: TipoDocumentoPuente.Devolucion, facturaOrigenId: d.FacturaProveedorOrigenId);
    }

    /// <summary>
    /// #13 AS: faltante (ajuste negativo) cargo AJUSTE_INVENTARIO o MERMA · abono INVENTARIO; sobrante al revés. MERMA solo si el
    /// motivo lo dice (hoy Almacén no publica motivo ni centro de costo: todo va a AJUSTE_INVENTARIO).
    /// </summary>
    private static EventoContable Ajuste(Guid eventoId, string payload)
    {
        var a = Leer<AjusteContrato>(payload);
        var movimientos = a.MovimientosGenerados ?? [];
        var faltante = movimientos.Where(m => EsNegativo(m.Tipo)).Sum(m => Math.Abs(m.MontoMxn));
        var sobrante = movimientos.Where(m => !EsNegativo(m.Tipo)).Sum(m => Math.Abs(m.MontoMxn));
        var esMerma = a.Motivo?.Contains("merma", StringComparison.OrdinalIgnoreCase) == true;
        return Mxn(AjusteInventarioAplicado, eventoId, a.EmpresaId, FechaContable.HoyLocal(a.OcurridoEn),
            Doc(DocAjuste, a.ConteoId, null), a.SucursalId, centroCostoId: a.CentroCostoId,
            importes: new()
            {
                ["Faltante"] = esMerma ? 0m : faltante,
                ["FaltanteMerma"] = esMerma ? faltante : 0m,
                ["FaltanteTotal"] = faltante,
                ["Sobrante"] = sobrante,
            },
            lineas: [], payload: payload);

        static bool EsNegativo(string? tipo) => tipo?.Contains("Negativo", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>#14 SM: cargo CONSUMO por línea con su centro de costo · abono INVENTARIO.</summary>
    private static EventoContable Salida(Guid eventoId, string payload)
    {
        var s = Leer<SalidaContrato>(payload);
        var lineas = (s.Lineas ?? []).Select(l => new LineaEventoContable(null, l.MontoTotalMxn, l.CentroCostoId, false)).ToList();
        return Mxn(SalidaRequisicionRegistrada, eventoId, s.EmpresaId, s.FechaMovimiento, Doc(DocSalida, s.SalidaId, s.FolioSalida),
            s.SucursalId, importes: new() { ["Subtotal"] = lineas.Sum(l => l.Importe) }, lineas: lineas, payload: payload);
    }

    // ---------------------------------------------------------------- CxP

    /// <summary>
    /// #3b y #3c PC: cargo PROVEEDORES · abono puente (Subtotal) + IVA acreditable pendiente. El evento no trae la OC: la referencia del
    /// puente va por la factura origen.
    /// </summary>
    private static EventoContable NotaProveedor(string tipo, Guid eventoId, string payload, (string Modulo, string Tipo) doc, TipoDocumentoPuente puente)
    {
        var n = Leer<NotaProveedorContrato>(payload);
        var total = n.Total ?? n.Monto;
        var retenciones = n.RetencionesTotal ?? 0m;
        return new(tipo, eventoId, n.EmpresaId, FechaContable.HoyLocal(n.OcurridoEn),
            Doc(doc, n.NotaCreditoId ?? n.NotaCargoId ?? Guid.Empty, null, n.Uuid), n.SucursalId, n.ProveedorId, null, null, null,
            MonedaDe(n.Moneda), n.TipoCambio,
            new Dictionary<string, decimal?>
            {
                ["Subtotal"] = n.Subtotal, ["Iva"] = n.Iva, ["RetencionesTotal"] = retenciones, ["Total"] = total,
                ["TotalMenosRetenciones"] = total - retenciones,
            },
            [], payload, puente, n.FacturaOrigenId);
    }

    /// <summary>#4 AN: cargo ANTICIPO_A_PROVEEDORES · abono BANCOS.</summary>
    private static EventoContable AnticipoProveedor(Guid eventoId, string payload)
    {
        var a = Leer<AnticipoProveedorContrato>(payload);
        return new(AnticipoProveedorCapturado, eventoId, a.EmpresaId, FechaContable.HoyLocal(a.OcurridoEn),
            Doc(DocAnticipoProveedor, a.AnticipoId, null), a.SucursalId, a.ProveedorId, null, null, null,
            MonedaDe(a.Moneda), a.TipoCambio, new Dictionary<string, decimal?> { ["Total"] = a.MontoEntregado }, [], payload);
    }

    // ---------------------------------------------------------------- Tesorería

    /// <summary>#5 PP: cargo PROVEEDORES · abono BANCOS. Proveedor y sucursal: de la póliza TT de la factura (enriquecedor).</summary>
    private static EventoContable PagoProveedor(Guid eventoId, string payload)
    {
        var p = Leer<PagoProveedorContrato>(payload);
        return new(PagoProveedorAplicado, eventoId, p.EmpresaId, p.FechaPago, Doc(DocPagoProveedor, p.PagoId, p.ReferenciaBancaria),
            null, null, null, null, null, MonedaDe(p.Moneda), p.TipoCambio,
            new Dictionary<string, decimal?> { ["Total"] = p.Monto }, [], payload, FacturaOrigenId: p.FacturaProveedorId);
    }

    /// <summary>#12 PR: cargo BANCOS · abono CLIENTES. Sucursal: de la póliza RF de la primera factura pagada (enriquecedor).</summary>
    private static EventoContable PagoCliente(Guid eventoId, string payload)
    {
        var p = Leer<PagoClienteContrato>(payload);
        return new(PagoClienteConfirmado, eventoId, p.EmpresaId, p.FechaValor, Doc(DocPagoCliente, p.MovimientoBancarioId, p.Referencia),
            null, null, p.ClienteId, null, null, MonedaDe(p.Moneda), null,
            new Dictionary<string, decimal?> { ["Total"] = p.Monto }, [], payload,
            FacturaOrigenId: p.Facturas is { Count: > 0 } facturas ? facturas[0].FacturaVentaId : null);
    }

    // ---------------------------------------------------------------- Facturación

    /// <summary>
    /// #7 RF: cargo CLIENTES (total menos retenciones) + retenciones a favor · abono VENTAS por línea (neto de descuento; rol por tipo de
    /// producto si existe) + IVA trasladado no cobrado.
    /// </summary>
    private static EventoContable FacturaVenta(Guid eventoId, string payload)
    {
        var f = Leer<FacturaVentaContrato>(payload);
        var retenciones = f.RetencionesTotal ?? 0m;
        var lineas = (f.Lineas ?? [])
            .Select(l => new LineaEventoContable(null, l.Importe - l.Descuento, null, false, Clasificador: l.TipoProducto)).ToList();
        return new(FacturaVentaTimbrada, eventoId, f.EmpresaId, f.FechaContable ?? FechaContable.HoyLocal(f.OcurridoEn),
            Doc(DocComprobante, f.FacturaVentaId, f.Folio, f.Uuid), f.SucursalId, null, f.ClienteId, null, null, MonedaDe(f.Moneda),
            f.TipoCambio,
            new Dictionary<string, decimal?>
            {
                ["Subtotal"] = f.Subtotal is { } s ? s - (f.Descuento ?? 0m) : null, ["Iva"] = f.Iva, ["RetencionesTotal"] = retenciones,
                ["Total"] = f.Total, ["TotalMenosRetenciones"] = f.Total - retenciones,
            },
            lineas, payload);
    }

    /// <summary>#8 AN: cargo CLIENTES · abono ANTICIPOS_DE_CLIENTES + IVA trasladado no cobrado.</summary>
    private static EventoContable FacturaAnticipo(Guid eventoId, string payload)
    {
        var a = Leer<FacturaAnticipoContrato>(payload);
        return new(FacturaAnticipoTimbrada, eventoId, a.EmpresaId, a.FechaContable ?? FechaContable.HoyLocal(a.OcurridoEn),
            Doc(DocComprobante, a.FacturaAnticipoId, null, a.Uuid), a.SucursalId, null, a.ClienteId, null, null, MonedaDe(a.Moneda),
            a.TipoCambio, new Dictionary<string, decimal?> { ["Subtotal"] = a.Subtotal, ["Iva"] = a.Iva, ["Total"] = a.Total }, [], payload);
    }

    /// <summary>
    /// #9 RC: descuento (Ranura y demás motivos) cargo DESCUENTOS_SOBRE_VENTAS + IVA · abono CLIENTES; aplicación de anticipo (trae
    /// <c>AnticipoOrigenId</c>) cargo ANTICIPOS_DE_CLIENTES · abono CLIENTES por el total (regla 3 de la ficha, D10).
    /// </summary>
    private static EventoContable NotaCreditoVenta(Guid eventoId, string payload)
    {
        var n = Leer<NotaCreditoVentaContrato>(payload);
        var anticipo = n.AnticipoOrigenId is not null;
        return new(NotaCreditoTimbrada, eventoId, n.EmpresaId, n.FechaContable ?? FechaContable.HoyLocal(n.OcurridoEn),
            Doc(DocComprobante, n.NotaCreditoId, null, n.Uuid), n.SucursalId, null, n.ClienteId, null, null, MonedaDe(n.Moneda),
            n.TipoCambio,
            new Dictionary<string, decimal?>
            {
                ["SubtotalDescuento"] = anticipo ? 0m : n.Subtotal,
                ["IvaDescuento"] = anticipo ? 0m : n.Iva,
                ["TotalDescuento"] = anticipo ? 0m : n.Total,
                ["TotalAnticipo"] = anticipo ? n.Total : 0m,
            },
            [], payload);
    }

    /// <summary>
    /// #11a PR: la parte del cobro que NO es efectivo, una póliza por cobro: tarjeta → COBROS_TC_TD, transferencia → BANCOS_MN, cheque
    /// → COBROS_CHEQUE, otra forma → COBROS_OTRAS_FORMAS · abono CLIENTES. El efectivo va a la liquidación de la sesión (#11b).
    /// </summary>
    private static EventoContable CobroNoEfectivo(Guid eventoId, string payload)
    {
        var c = Leer<CobroRegistradoContrato>(payload);
        var formas = c.FormasPago ?? [];
        decimal Suma(params string[] claves) => formas.Where(f => claves.Contains(f.FormaPago)).Sum(f => f.Importe);
        var tarjeta = Suma(FormaTarjetaCredito, FormaTarjetaDebito);
        var transferencia = Suma(FormaTransferencia);
        var cheque = Suma(FormaCheque);
        var otra = formas.Where(f => f.FormaPago is not (FormaEfectivo or FormaCheque or FormaTransferencia or FormaTarjetaCredito or FormaTarjetaDebito))
            .Sum(f => f.Importe);
        return new(CobroMostradorRegistrado, eventoId, c.EmpresaId, FechaContable.HoyLocal(c.OcurridoEn),
            Doc(DocCobro, c.CobroMostradorId, null), c.SucursalId, null, c.ClienteId, null, null, MonedaDe(c.Moneda), c.TipoCambio,
            new Dictionary<string, decimal?>
            {
                ["Tarjeta"] = tarjeta, ["Transferencia"] = transferencia, ["Cheque"] = cheque, ["OtraForma"] = otra,
                ["TotalNoEfectivo"] = tarjeta + transferencia + cheque + otra,
            },
            [], payload);
    }

    /// <summary>
    /// #11b PR: liquidación de efectivo de una sesión de caja (cajero, día y sucursal): cargo EFECTIVO_CAJA_COBRANZA por el total ·
    /// un abono a CLIENTES por cobro con su cliente. <paramref name="llave"/> distinta para la reversa de un cobro ya liquidado.
    /// </summary>
    public static EventoContable Liquidacion(Guid eventoId, Guid empresaId, DateOnly fecha, (string Modulo, string Tipo) doc, Guid documentoId,
        string? folio, Guid? sucursalId, IReadOnlyList<(Guid? ClienteId, decimal Importe)> cobros, string payload, string? llave = null)
    {
        var lineas = cobros.Select(c => new LineaEventoContable(null, c.Importe, null, false, c.ClienteId)).ToList();
        return new(CajaSesionCerrada, eventoId, empresaId, fecha, Doc(doc, documentoId, folio), sucursalId, null, null, null, null,
            "MXN", null, new Dictionary<string, decimal?> { ["Total"] = lineas.Sum(l => l.Importe) }, lineas, payload,
            LlaveOverride: llave);
    }

    // ---------------------------------------------------------------- Apoyo

    private static T Leer<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, Json) ?? throw new EventoIlegibleException("Payload vacío.");

    internal static T LeerContrato<T>(string payload) => Leer<T>(payload);

    private static DocumentoOrigenPoliza Doc((string Modulo, string Tipo) doc, Guid id, string? folio, string? uuid = null) =>
        new(doc.Modulo, doc.Tipo, id, string.IsNullOrWhiteSpace(folio) ? null : folio.Trim(), string.IsNullOrWhiteSpace(uuid) ? null : uuid);

    private static string MonedaDe(string? moneda) => string.IsNullOrWhiteSpace(moneda) ? "MXN" : moneda.Trim().ToUpperInvariant();

    private static EventoContable Mxn(string tipo, Guid eventoId, Guid empresaId, DateOnly fecha, DocumentoOrigenPoliza doc, Guid? sucursalId,
        Dictionary<string, decimal?> importes, IReadOnlyList<LineaEventoContable> lineas, string payload, Guid? proveedorId = null,
        Guid? centroCostoId = null, Guid? ordenCompraId = null, TipoDocumentoPuente tipoPuente = TipoDocumentoPuente.Factura,
        Guid? facturaOrigenId = null) =>
        new(tipo, eventoId, empresaId, fecha, doc, sucursalId, proveedorId, null, centroCostoId,
            ordenCompraId == Guid.Empty ? null : ordenCompraId, "MXN", null, importes, lineas, payload, tipoPuente, facturaOrigenId);

    // ---- Espejos de contrato (solo los campos que usa Contabilidad) ----

    private sealed record RecepcionRegistradaContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid RecepcionId, string? FolioRecepcion,
        Guid OrdenCompraId, DateOnly FechaMovimiento, IReadOnlyList<LineaRecepcionContrato>? Lineas, Guid? SucursalId);

    private sealed record LineaRecepcionContrato(Guid? LineaOcId, decimal MontoTotalMxn, bool EsActivoFijo, Guid? CentroCostoId);

    private sealed record RecepcionCanceladaContrato(Guid RecepcionId, string? MotivoTexto);

    private sealed record DevolucionProveedorContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid DevolucionId, string? FolioMovimiento,
        Guid ProveedorId, Guid? FacturaProveedorOrigenId, Guid? OrdenCompraOrigenId, IReadOnlyList<LineaDevolucionProveedorContrato>? Lineas,
        Guid? SucursalId);

    private sealed record LineaDevolucionProveedorContrato(Guid? LineaOcId, decimal MontoTotalMxn);

    private sealed record AjusteContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid ConteoId,
        IReadOnlyList<AjusteMovimientoContrato>? MovimientosGenerados, Guid? SucursalId, Guid? CentroCostoId, string? Motivo);

    private sealed record AjusteMovimientoContrato(string? Tipo, decimal MontoMxn);

    private sealed record SalidaContrato(Guid EmpresaId, Guid SalidaId, string? FolioSalida, DateOnly FechaMovimiento,
        IReadOnlyList<LineaSalidaContrato>? Lineas, Guid? SucursalId);

    internal sealed record LineaSalidaContrato(Guid LineaSalidaId, Guid ArticuloId, decimal MontoTotalMxn, Guid? CentroCostoId);

    /// <summary>Salida de requisición para registrar el consumo contabilizado por línea (#15a, V64).</summary>
    internal sealed record SalidaLineasContrato(Guid SalidaId, IReadOnlyList<LineaSalidaContrato>? Lineas);

    internal sealed record DevolucionInternaContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid DevolucionId, string? FolioDevolucion,
        Guid SalidaOrigenId, IReadOnlyList<LineaDevolucionInternaContrato>? Lineas, Guid? SucursalId);

    internal sealed record LineaDevolucionInternaContrato(Guid ArticuloId, decimal MontoTotalMxn);

    private sealed record FacturaProveedorCanceladaContrato(Guid FacturaProveedorId, string? Motivo, string? MotivoTexto);

    private sealed record NotaProveedorContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid? NotaCreditoId, Guid? NotaCargoId,
        Guid ProveedorId, Guid? FacturaOrigenId, decimal? Total, decimal? Monto, string? Uuid, decimal? Subtotal, decimal? Iva,
        decimal? RetencionesTotal, string? Moneda, decimal? TipoCambio, Guid? SucursalId);

    private sealed record AnticipoProveedorContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid AnticipoId, Guid ProveedorId,
        decimal MontoEntregado, string? Moneda, decimal? TipoCambio, Guid? SucursalId);

    private sealed record PagoProveedorContrato(Guid EmpresaId, Guid FacturaProveedorId, Guid PagoId, decimal Monto, string? Moneda,
        DateOnly FechaPago, string? ReferenciaBancaria, decimal? TipoCambio);

    private sealed record PagoRevertidoContrato(Guid PagoOriginalId, string? Motivo);

    private sealed record PagoClienteContrato(Guid EmpresaId, Guid ClienteId, Guid MovimientoBancarioId, decimal Monto, string? Moneda,
        DateOnly FechaValor, string? Referencia, IReadOnlyList<PagoClienteFacturaContrato>? Facturas);

    private sealed record PagoClienteFacturaContrato(Guid FacturaVentaId, decimal ImporteAplicado);

    private sealed record FacturaVentaContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid FacturaVentaId, string? Uuid, decimal Total,
        string? Moneda, string? Folio, decimal? Subtotal, decimal? Descuento, decimal? Iva, decimal? RetencionesTotal, Guid? SucursalId,
        Guid? ClienteId, decimal? TipoCambio, DateOnly? FechaContable, IReadOnlyList<FacturaLineaContrato>? Lineas);

    private sealed record FacturaLineaContrato(string? TipoProducto, decimal Importe, decimal Descuento);

    private sealed record FacturaAnticipoContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid FacturaAnticipoId, string? Uuid, decimal Total,
        string? Moneda, decimal? Subtotal, decimal? Iva, Guid? SucursalId, Guid? ClienteId, decimal? TipoCambio, DateOnly? FechaContable);

    internal sealed record NotaCreditoVentaContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid NotaCreditoId, string? Uuid, decimal Total,
        Guid? AnticipoOrigenId, decimal? Subtotal, decimal? Iva, Guid? SucursalId, Guid? ClienteId, string? Moneda, decimal? TipoCambio,
        DateOnly? FechaContable, Guid? FacturaRelacionadaId);

    private sealed record ComprobanteCanceladoContrato(Guid ComprobanteId);

    internal sealed record CobroRegistradoContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid CobroMostradorId, Guid CajaSesionId,
        IReadOnlyList<CobroFormaContrato>? FormasPago, Guid? SucursalId, Guid? ClienteId, string? Moneda, decimal? TipoCambio, Guid? ComprobanteId);

    internal sealed record CobroFormaContrato(string FormaPago, decimal Importe);

    internal sealed record CobroCanceladoContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid CobroMostradorId, bool ReversadoEnSesion);

    internal sealed record CajaSesionCerradaContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid CajaSesionId, Guid SucursalId,
        DateOnly DiaOperacion);
}
