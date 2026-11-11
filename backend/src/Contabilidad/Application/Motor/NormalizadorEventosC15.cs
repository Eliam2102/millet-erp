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
    public const string SalidaInventarioRegistrada = "almacen.salida_inventario.registrada.v1";
    public const string EntradaInventarioRegistrada = "almacen.entrada_inventario.registrada.v1";
    public const string SalidaRoturaRegistrada = "almacen.salida_rotura.registrada.v1";
    public const string DevolucionInternaAplicada = "almacen.devolucion_interna.aplicada.v1";
    public const string DevolucionProveedorRegistrada = "almacen.oc_devolucion.registrada.v1";
    public const string FacturaProveedorCancelada = "cuentas_por_pagar.factura.cancelada.v1";
    public const string NotaCreditoProveedorRegistrada = "cuentas_por_pagar.nota-credito.registrada.v1";
    public const string NotaCargoAutorizada = "cuentas_por_pagar.nota-cargo.autorizada.v1";
    public const string AnticipoProveedorCapturado = "cuentas_por_pagar.anticipo.capturado.v1";
    public const string RevaluacionCierreRegistrada = "cuentas_por_pagar.revaluacion.registrada.v1";
    public const string RevaluacionCierreRevertida = "cuentas_por_pagar.revaluacion.revertida.v1";
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
    public static readonly (string Modulo, string Tipo) DocSalidaInventario = ("almacen", "salida_inventario");
    public static readonly (string Modulo, string Tipo) DocEntradaInventario = ("almacen", "entrada_inventario");
    public static readonly (string Modulo, string Tipo) DocDevolucionInterna = ("almacen", "devolucion_interna");
    public static readonly (string Modulo, string Tipo) DocDevolucionProveedor = ("almacen", "devolucion_proveedor");
    public static readonly (string Modulo, string Tipo) DocFacturaProveedor = ("cuentas_por_pagar", "factura_proveedor");
    public static readonly (string Modulo, string Tipo) DocNotaCreditoProveedor = ("cuentas_por_pagar", "nota_credito_proveedor");
    public static readonly (string Modulo, string Tipo) DocNotaCargo = ("cuentas_por_pagar", "nota_cargo");
    public static readonly (string Modulo, string Tipo) DocAnticipoProveedor = ("cuentas_por_pagar", "anticipo_proveedor");
    public static readonly (string Modulo, string Tipo) DocRevaluacionCierre = ("cuentas_por_pagar", "revaluacion_cierre");
    public static readonly (string Modulo, string Tipo) DocRevaluacionCierreReversa = ("cuentas_por_pagar", "revaluacion_cierre_reversa");
    public static readonly (string Modulo, string Tipo) DocPagoProveedor =("tesoreria", "pago_factura_proveedor");
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
        SalidaInventarioRegistrada => SalidaInventario(eventoId, payload),
        EntradaInventarioRegistrada => EntradaInventario(eventoId, payload),
        SalidaRoturaRegistrada => SalidaRotura(eventoId, payload),
        NotaCreditoProveedorRegistrada => NotaProveedor(tipo, eventoId, payload, DocNotaCreditoProveedor, TipoDocumentoPuente.NotaCredito),
        NotaCargoAutorizada => NotaProveedor(tipo, eventoId, payload, DocNotaCargo, TipoDocumentoPuente.NotaCargo),
        AnticipoProveedorCapturado => AnticipoProveedor(eventoId, payload),
        RevaluacionCierreRegistrada or RevaluacionCierreRevertida => RevaluacionCierre(tipo, eventoId, payload),
        PagoAnticipoAgente => PagoProveedor(eventoId, payload),
        PagoProveedorAplicado => PagoProveedor(eventoId, payload),
        PagoClienteConfirmado => PagoCliente(eventoId, payload),
        FacturaVentaTimbrada => FacturaVenta(eventoId, payload),
        FacturaAnticipoTimbrada => FacturaAnticipo(eventoId, payload),
        NotaCreditoTimbrada => NotaCreditoVenta(eventoId, payload),
        CobroMostradorRegistrado => CobroNoEfectivo(eventoId, payload),
        _ => NormalizarU2(tipo, eventoId, payload),
    };

    // ---------------------------------------------------------------- Almacén

    /// <summary>#1 EM: cargo INVENTARIO · abono puente por línea de OC. Las líneas de activo fijo no entran (C1.5-h).</summary>
    private static EventoContable Recepcion(Guid eventoId, string payload)
    {
        var r = Leer<RecepcionRegistradaContrato>(payload);
        var lineas = (r.Lineas ?? []).Where(l => !l.EsActivoFijo)
            .Select(l => new LineaEventoContable(l.LineaOcId, l.MontoTotalMxn, l.CentroCostoId, false, ArticuloId: l.ArticuloId)).ToList();
        return Mxn(RecepcionOcRegistrada, eventoId, r.EmpresaId, r.FechaMovimiento,
            Doc(DocRecepcion, r.RecepcionId, r.FolioRecepcion), r.SucursalId, ordenCompraId: r.OrdenCompraId,
            importes: new() { ["Subtotal"] = lineas.Sum(l => l.Importe) }, lineas: lineas, payload: payload,
            tipoPuente: TipoDocumentoPuente.Recepcion);
    }

    /// <summary>#15b EM: devolución a proveedor; cargo puente por línea de OC · abono INVENTARIO (la nota de cargo #3c salda el puente).</summary>
    private static EventoContable DevolucionProveedor(Guid eventoId, string payload)
    {
        var d = Leer<DevolucionProveedorContrato>(payload);
        var lineas = (d.Lineas ?? []).Select(l => new LineaEventoContable(l.LineaOcId, l.MontoTotalMxn, null, false, ArticuloId: l.ArticuloId)).ToList();
        return Mxn(DevolucionProveedorRegistrada, eventoId, d.EmpresaId, FechaContable.HoyLocal(d.OcurridoEn),
            Doc(DocDevolucionProveedor, d.DevolucionId, d.FolioMovimiento), d.SucursalId, proveedorId: d.ProveedorId,
            ordenCompraId: d.OrdenCompraOrigenId, importes: new() { ["Subtotal"] = lineas.Sum(l => l.Importe) }, lineas: lineas,
            payload: payload, tipoPuente: TipoDocumentoPuente.Devolucion, facturaOrigenId: d.FacturaProveedorOrigenId);
    }

    /// <summary>
    /// #13 AS: faltante (ajuste negativo) cargo AJUSTE_INVENTARIO o MERMA · abono INVENTARIO; sobrante al revés. MERMA solo si el
    /// motivo lo dice (hoy Almacén no publica motivo ni centro de costo: todo va a AJUSTE_INVENTARIO).
    /// F1-ALM-13: una línea por movimiento con signo (sobrante +, faltante −) para el inventario por grupo de artículo (plantilla v2).
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
                ["Neto"] = sobrante - faltante,
            },
            lineas: [.. movimientos.Select(m => new LineaEventoContable(null, EsNegativo(m.Tipo) ? -Math.Abs(m.MontoMxn) : Math.Abs(m.MontoMxn),
                null, false, ArticuloId: m.ArticuloId))],
            payload: payload);

        static bool EsNegativo(string? tipo) => tipo?.Contains("Negativo", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>#14 SM: cargo CONSUMO por línea con su centro de costo · abono INVENTARIO.</summary>
    private static EventoContable Salida(Guid eventoId, string payload)
    {
        var s = Leer<SalidaContrato>(payload);
        var lineas = (s.Lineas ?? []).Select(l => new LineaEventoContable(null, l.MontoTotalMxn, l.CentroCostoId, false, ArticuloId: l.ArticuloId)).ToList();
        return Mxn(SalidaRequisicionRegistrada, eventoId, s.EmpresaId, s.FechaMovimiento, Doc(DocSalida, s.SalidaId, s.FolioSalida),
            s.SucursalId, importes: new() { ["Subtotal"] = lineas.Sum(l => l.Importe) }, lineas: lineas, payload: payload);
    }

    /// <summary>
    /// Campo de importe que carga una salida de inventario según su causa (U-2): rotura o daño → Merma; salida manual o no planificada →
    /// Ajuste; lo demás → Consumo. H1 (86e3z5w25) lo usa también para saber qué cuenta, y qué dimensiones, exige la salida al capturarla.
    /// </summary>
    public static string CampoSalidaInventario(string? causa) => (causa ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "rotura" or "daño" or "dano" => "Merma",
        "salida_manual" or "salida_no_planificada" => "Ajuste",
        _ => "Consumo",
    };

    /// <summary>
    /// Cuenta por motivo de la salida manual de la app (Fernando Alejos, 10-nov-2026): FALTANTE → Ajuste (AJUSTE_INVENTARIO, 504.25.02.00),
    /// MUESTRA → Muestra (PRODUCCION_EN_PROCESO, 115.03.00.00), PRESTAMO → Prestamo (traspaso entre sucursales; INVENTARIO_EN_PRESTAMO),
    /// CONSUMO_INTERNO → Consumo (con su centro de costo). Sin clave o con otra (CANCELA_ENTRADA…) manda la causa, como antes.
    /// </summary>
    public static string CampoSalidaInventario(string? causa, string? motivoClave) => (motivoClave ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "FALTANTE" => "Ajuste",
        "MUESTRA" => "Muestra",
        "PRESTAMO" => "Prestamo",
        "CONSUMO_INTERNO" => "Consumo",
        _ => CampoSalidaInventario(causa),
    };

    /// <summary>
    /// G-1 (86e3yxpb3): salida de inventario sin requisición (consumo, rotura, salida manual, baja por daño). Misma póliza que #14 (cargo
    /// CONSUMO · abono INVENTARIO por línea). El centro de costo es el de cada línea o, si no, el del evento (equipo que ya capturó el vale); vacío si ninguno lo trae.
    /// </summary>
    private static EventoContable SalidaInventario(Guid eventoId, string payload)
    {
        var s = Leer<SalidaInventarioContrato>(payload);
        var lineas = (s.Lineas ?? []).Select(l => new LineaEventoContable(null, l.MontoMxn, l.CentroCostoId, false, ArticuloId: l.ArticuloId)).ToList();
        // U-2 (86e3yxngq): la plantilla v2 carga según la causa (consumo → CONSUMO, rotura o daño → MERMA, salida manual o no
        // planificada → AJUSTE_INVENTARIO). La v1 (#445) solo lee Subtotal.
        var total = lineas.Sum(l => l.Importe);
        var campo = CampoSalidaInventario(s.Causa, s.MotivoClave);
        var merma = campo == "Merma";
        var ajuste = campo == "Ajuste";
        var muestra = campo == "Muestra";
        var prestamo = campo == "Prestamo";
        return Mxn(SalidaInventarioRegistrada, eventoId, s.EmpresaId, s.FechaMovimiento, Doc(DocSalidaInventario, s.MovimientoId, s.FolioMovimiento),
            s.SucursalId, importes: new()
            {
                ["Subtotal"] = total,
                ["Consumo"] = merma || ajuste || muestra || prestamo ? 0m : total,
                ["Merma"] = merma ? total : 0m,
                ["Ajuste"] = ajuste ? total : 0m,
                ["Muestra"] = muestra ? total : 0m,
                ["Prestamo"] = prestamo ? total : 0m,
            }, lineas: lineas, payload: payload, centroCostoId: s.CentroCostoId);
    }

    /// <summary>Grupo de las líneas de una entrada de producto terminado (devolución de venta): cargo INVENTARIO_PT en vez de INVENTARIO.</summary>
    public const string GrupoInventarioPt = "PT";

    /// <summary>
    /// Entrada manual de la app (Fernando Alejos, 10-nov-2026), al costo promedio del ERP. Cargo inventario por línea (por grupo del artículo;
    /// INVENTARIO_PT en la devolución de venta) contra: SOBRANTE y SIN_COMPRA → MERMA (504.25.02.00); DEVOLUCION_VENTA → COSTO_DE_VENTAS
    /// (revierte el costo con que salió; la NC es de Facturación); REGRESO_PRESTAMO → INVENTARIO_EN_PRESTAMO (traspaso entre sucursales).
    /// Clave desconocida: sin líneas ni importes, y el motor la rechaza; Almacén no publica claves sin póliza.
    /// </summary>
    private static EventoContable EntradaInventario(Guid eventoId, string payload)
    {
        var s = Leer<EntradaInventarioContrato>(payload);
        var clave = (s.MotivoClave ?? string.Empty).Trim().ToUpperInvariant();
        var conocida = clave is "SOBRANTE" or "SIN_COMPRA" or "DEVOLUCION_VENTA" or "REGRESO_PRESTAMO";
        var pt = clave == "DEVOLUCION_VENTA";
        var lineas = !conocida ? [] : (s.Lineas ?? []).Select(l => new LineaEventoContable(null, l.MontoMxn, null, false,
            ArticuloId: l.ArticuloId, Grupo: pt ? GrupoInventarioPt : null)).ToList();
        var total = lineas.Sum(l => l.Importe);
        return Mxn(EntradaInventarioRegistrada, eventoId, s.EmpresaId, s.FechaMovimiento, Doc(DocEntradaInventario, s.MovimientoId, s.FolioMovimiento),
            s.SucursalId, importes: new()
            {
                // Inventario e InventarioPt: solo si una línea de su grupo no existe (el motor los lee entonces); siempre cero.
                ["Inventario"] = 0m,
                ["InventarioPt"] = 0m,
                ["Merma"] = clave is "SOBRANTE" or "SIN_COMPRA" ? total : 0m,
                ["CostoVentas"] = pt ? total : 0m,
                ["Prestamo"] = clave == "REGRESO_PRESTAMO" ? total : 0m,
            }, lineas: lineas, payload: payload);
    }

    /// <summary>
    /// 86e3zf04y: rotura capturada en la pantalla de Almacén (ALM-03). Misma póliza que la rotura de la app de Almacenes
    /// (<see cref="SalidaInventario"/> con causa rotura): cargo MERMA con el área o equipo de la rotura · abono INVENTARIO por grupo del
    /// artículo, mismo documento origen (el movimiento de salida). Plantilla propia, sembrada igual que la v2 de salidas.
    /// </summary>
    private static EventoContable SalidaRotura(Guid eventoId, string payload)
    {
        var r = Leer<SalidaRoturaContrato>(payload);
        var valor = r.ValorMxn ?? 0m;
        return Mxn(SalidaRoturaRegistrada, eventoId, r.EmpresaId, r.FechaMovimiento, Doc(DocSalidaInventario, r.MovimientoId, r.FolioMovimiento),
            r.SucursalId, importes: new() { ["Subtotal"] = valor, ["Merma"] = valor },
            lineas: [new LineaEventoContable(null, valor, r.CentroCostoId, false, ArticuloId: r.ArticuloId)], payload: payload,
            centroCostoId: r.CentroCostoId);
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
                ["TotalMenosRetenciones"] = total, // el Total del CFDI ya viene neto de retenciones
                [RetencionesContables.CampoTotalMas] = total + retenciones, // 86e3zcbjw: #3b/#3c v3 (factura por el total)
            },
            // M-209: el IVA de la nota por tratamiento (revierte el pendiente de la tasa correspondiente); sin detalle, como hoy.
            ImpuestosTrasladados.LineasDocumento(ImpuestosTrasladados.Agrupar(n.Impuestos), n.Iva), payload, puente, n.FacturaOrigenId);
    }

    /// <summary>#4 AN: cargo ANTICIPO_A_PROVEEDORES · abono BANCOS.</summary>
    private static EventoContable AnticipoProveedor(Guid eventoId, string payload)
    {
        var a = Leer<AnticipoProveedorContrato>(payload);
        return new(AnticipoProveedorCapturado, eventoId, a.EmpresaId, FechaContable.HoyLocal(a.OcurridoEn),
            Doc(DocAnticipoProveedor, a.AnticipoId, null), a.SucursalId, a.ProveedorId, null, null, null,
            MonedaDe(a.Moneda), a.TipoCambio, new Dictionary<string, decimal?> { ["Total"] = a.MontoEntregado }, [], payload);
    }

    /// <summary>
    /// CXP-12 · revaluación de cierre (no realizada), una póliza por proveedor y sucursal. Registrada: fecha = último día del mes; pérdida = cargo
    /// pérdida cambiaria · abono PROVEEDORES, utilidad al revés. Revertida: el día 1 siguiente, con los lados contrarios (plantilla propia).
    /// </summary>
    private static EventoContable RevaluacionCierre(string tipo, Guid eventoId, string payload)
    {
        var r = Leer<RevaluacionCierreContrato>(payload);
        var reversa = tipo == RevaluacionCierreRevertida;
        return Mxn(tipo, eventoId, r.EmpresaId, (reversa ? r.FechaReversa : r.FechaCierre) ?? throw new EventoIlegibleException("El evento no trae la fecha."),
            Doc(reversa ? DocRevaluacionCierreReversa : DocRevaluacionCierre, r.RevaluacionId, null), r.SucursalId,
            new() { ["Perdida"] = r.Perdida, ["Utilidad"] = r.Utilidad }, [], payload, proveedorId: r.ProveedorId);
    }

    // ---------------------------------------------------------------- Tesorería

    /// <summary>#5 PP: cargo PROVEEDORES · abono BANCOS. Proveedor y sucursal: de la póliza TT de la factura (enriquecedor).</summary>
    private static EventoContable PagoProveedor(Guid eventoId, string payload)
    {
        var p = Leer<PagoProveedorContrato>(payload);
        if (p.EsAnticipoAgente)
            return new(PagoAnticipoAgente, eventoId, p.EmpresaId, p.FechaPago, Doc(DocPagoProveedor, p.PagoId, p.ReferenciaBancaria),
                p.SucursalId, p.ProveedorId, null, null, null, MonedaDe(p.Moneda), p.TipoCambio,
                new Dictionary<string, decimal?> { ["Total"] = p.Monto }, [], payload,
                LlaveOverride: $"{PagoAnticipoAgente}:{p.PagoId}");
        return new(PagoProveedorAplicado, eventoId, p.EmpresaId, p.FechaPago, Doc(DocPagoProveedor, p.PagoId, p.ReferenciaBancaria),
            null, null, null, null, null, MonedaDe(p.Moneda), p.TipoCambio,
            new Dictionary<string, decimal?> { ["Total"] = p.Monto }, [], payload, FacturaOrigenId: p.FacturaProveedorId);
    }

    /// <summary>
    /// #12 PR: cargo BANCOS por el depósito · abono CLIENTES por lo aplicado a cartera. Sucursal: de la póliza RF de la primera factura
    /// pagada (enriquecedor). CXC-06: con un ajuste autorizado lo aplicado es mayor que el depósito; la diferencia (<c>Ajuste</c>) la
    /// carga el enriquecedor a la cuenta de su tipo (<c>AjusteComision</c> o <c>AjusteDiferencia</c>), así cartera y póliza cuadran.
    /// Sin desglose de facturas, lo aplicado es el depósito (eventos anteriores).
    /// </summary>
    private static EventoContable PagoCliente(Guid eventoId, string payload)
    {
        var p = Leer<PagoClienteContrato>(payload);
        var aplicado = p.Facturas is { Count: > 0 } fs ? fs.Sum(f => f.ImporteAplicado) + p.SaldoAFavorPorIdentificar : p.Monto;
        var ajuste = Math.Max(0m, aplicado - p.Monto);
        return new(PagoClienteConfirmado, eventoId, p.EmpresaId, p.FechaValor, Doc(DocPagoCliente, p.MovimientoBancarioId, p.Referencia),
            null, null, p.ClienteId, null, null, MonedaDe(p.Moneda), null,
            new Dictionary<string, decimal?>
            {
                ["Total"] = p.Monto, ["Aplicado"] = p.Monto + ajuste, ["Ajuste"] = ajuste,
                ["AjusteComision"] = 0m, ["AjusteDiferencia"] = 0m, ["AjusteRedondeo"] = 0m,
            },
            [], payload,
            FacturaOrigenId: p.Facturas is { Count: > 0 } facturas ? facturas[0].FacturaVentaId : null,
            CuentaPorRol: CuentaBanco(p.CuentaContableRef));
    }

    /// <summary>
    /// B8: el cargo a bancos va a la cuenta contable de la cuenta bancaria del depósito (Tesorería la manda en el evento), no a la
    /// única del rol BANCOS. Sin dato (cuenta bancaria sin cuenta contable o evento anterior), se usa el rol.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? CuentaBanco(string? cuentaContableRef) =>
        FormatoCatalogo.Codigo(cuentaContableRef) is { } codigo ? new Dictionary<string, string> { [RolBancos] = codigo } : null;

    public const string RolBancos = "BANCOS";

    /// <summary>CXC-06: propuesta de CxC del pago de cliente (para leer su ajuste autorizado).</summary>
    internal static Guid? PropuestaDelPagoCliente(string payload) => Leer<PagoClienteContrato>(payload).PropuestaId;

    // ---------------------------------------------------------------- Facturación

    /// <summary>
    /// #7 RF: cargo CLIENTES (el Total del CFDI, ya neto de retenciones) + retenciones a favor · abono VENTAS por línea (neto de descuento; rol por tipo de
    /// producto si existe) + IVA trasladado no cobrado.
    /// </summary>
    private static EventoContable FacturaVenta(Guid eventoId, string payload)
    {
        var f = Leer<FacturaVentaContrato>(payload);
        var retenciones = f.RetencionesTotal ?? 0m;
        var lineas = (f.Lineas ?? [])
            .Select(l => new LineaEventoContable(null, l.Importe - l.Descuento, null, false, Clasificador: l.TipoProducto)).ToList();
        // 86e3zcbjw: una línea por concepto de retención del cliente (fletes, servicios, desperdicio, importación) → su cuenta (#7 v3).
        lineas.AddRange(RetencionesContables.Lineas(f.Retenciones, null));
        // M-209: IVA trasladado por tratamiento (p. ej. TASA8 en zona fronteriza) a su rol; sin detalle, como hoy.
        lineas.AddRange(ImpuestosTrasladados.LineasDocumento(ImpuestosTrasladados.Agrupar(f.Impuestos), f.Iva));
        return new(FacturaVentaTimbrada, eventoId, f.EmpresaId, f.FechaContable ?? FechaContable.HoyLocal(f.OcurridoEn),
            Doc(DocComprobante, f.FacturaVentaId, f.Folio, f.Uuid), f.SucursalId, null, f.ClienteId, null, null, MonedaDe(f.Moneda),
            f.TipoCambio,
            new Dictionary<string, decimal?>
            {
                ["Subtotal"] = f.Subtotal is { } s ? s - (f.Descuento ?? 0m) : null, ["Iva"] = f.Iva, ["RetencionesTotal"] = retenciones,
                ["Total"] = f.Total, ["TotalMenosRetenciones"] = f.Total, // el Total del CFDI ya viene neto de retenciones
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

    private sealed record LineaRecepcionContrato(Guid? LineaOcId, decimal MontoTotalMxn, bool EsActivoFijo, Guid? CentroCostoId, Guid? ArticuloId);

    private sealed record RecepcionCanceladaContrato(Guid RecepcionId, string? MotivoTexto);

    private sealed record DevolucionProveedorContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid DevolucionId, string? FolioMovimiento,
        Guid ProveedorId, Guid? FacturaProveedorOrigenId, Guid? OrdenCompraOrigenId, IReadOnlyList<LineaDevolucionProveedorContrato>? Lineas,
        Guid? SucursalId);

    private sealed record LineaDevolucionProveedorContrato(Guid? LineaOcId, decimal MontoTotalMxn, Guid? ArticuloId);

    private sealed record AjusteContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid ConteoId,
        IReadOnlyList<AjusteMovimientoContrato>? MovimientosGenerados, Guid? SucursalId, Guid? CentroCostoId, string? Motivo);

    private sealed record AjusteMovimientoContrato(string? Tipo, decimal MontoMxn, Guid? ArticuloId);

    private sealed record SalidaContrato(Guid EmpresaId, Guid SalidaId, string? FolioSalida, DateOnly FechaMovimiento,
        IReadOnlyList<LineaSalidaContrato>? Lineas, Guid? SucursalId);

    private sealed record SalidaInventarioContrato(Guid EmpresaId, Guid MovimientoId, string? FolioMovimiento, DateOnly FechaMovimiento,
        IReadOnlyList<LineaSalidaInventarioContrato>? Lineas, Guid? SucursalId, Guid? CentroCostoId, string? Causa = null, string? MotivoClave = null);

    private sealed record LineaSalidaInventarioContrato(Guid ArticuloId, decimal MontoMxn, Guid? CentroCostoId);

    private sealed record EntradaInventarioContrato(Guid EmpresaId, Guid MovimientoId, string? FolioMovimiento, DateOnly FechaMovimiento,
        string? MotivoClave, IReadOnlyList<LineaSalidaInventarioContrato>? Lineas, Guid? SucursalId);

    /// <summary>Espejo de <c>almacen.salida_rotura.registrada.v1</c> (ALM-03) con solo lo que usa la póliza de merma (86e3zf04y).</summary>
    private sealed record SalidaRoturaContrato(Guid EmpresaId, Guid MovimientoId, string? FolioMovimiento, DateOnly FechaMovimiento,
        Guid? SucursalId, Guid ArticuloId, decimal? ValorMxn, Guid? CentroCostoId);

    internal sealed record LineaSalidaContrato(Guid LineaSalidaId, Guid ArticuloId, decimal MontoTotalMxn, Guid? CentroCostoId);

    /// <summary>Salida de requisición para registrar el consumo contabilizado por línea (#15a, V64).</summary>
    internal sealed record SalidaLineasContrato(Guid SalidaId, IReadOnlyList<LineaSalidaContrato>? Lineas);

    internal sealed record DevolucionInternaContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid DevolucionId, string? FolioDevolucion,
        Guid SalidaOrigenId, IReadOnlyList<LineaDevolucionInternaContrato>? Lineas, Guid? SucursalId);

    internal sealed record LineaDevolucionInternaContrato(Guid ArticuloId, decimal MontoTotalMxn);

    private sealed record FacturaProveedorCanceladaContrato(Guid FacturaProveedorId, string? Motivo, string? MotivoTexto);

    private sealed record NotaProveedorContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid? NotaCreditoId, Guid? NotaCargoId,
        Guid ProveedorId, Guid? FacturaOrigenId, decimal? Total, decimal? Monto, string? Uuid, decimal? Subtotal, decimal? Iva,
        decimal? RetencionesTotal, string? Moneda, decimal? TipoCambio, Guid? SucursalId,
        IReadOnlyList<ImpuestosTrasladados.ImpuestoTrasladoContrato>? Impuestos = null);

    private sealed record AnticipoProveedorContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid AnticipoId, Guid ProveedorId,
        decimal MontoEntregado, string? Moneda, decimal? TipoCambio, Guid? SucursalId);

    private sealed record RevaluacionCierreContrato(Guid EmpresaId, Guid RevaluacionId, Guid ProveedorId, Guid? SucursalId, DateOnly? FechaCierre, DateOnly? FechaReversa,
        decimal? Perdida, decimal? Utilidad);

    private sealed record PagoProveedorContrato(Guid EmpresaId, Guid FacturaProveedorId, Guid PagoId, decimal Monto, string? Moneda,
        DateOnly FechaPago, string? ReferenciaBancaria, decimal? TipoCambio, bool EsAnticipoAgente = false, Guid? SucursalId = null, Guid? ProveedorId = null);

    private sealed record PagoRevertidoContrato(Guid PagoOriginalId, string? Motivo);

    private sealed record PagoClienteContrato(Guid EmpresaId, Guid ClienteId, Guid MovimientoBancarioId, decimal Monto, string? Moneda,
        DateOnly FechaValor, string? Referencia, IReadOnlyList<PagoClienteFacturaContrato>? Facturas, Guid? PropuestaId = null,
        decimal SaldoAFavorPorIdentificar = 0m, string? CuentaContableRef = null);

    private sealed record PagoClienteFacturaContrato(Guid FacturaVentaId, decimal ImporteAplicado);

    private sealed record FacturaVentaContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid FacturaVentaId, string? Uuid, decimal Total,
        string? Moneda, string? Folio, decimal? Subtotal, decimal? Descuento, decimal? Iva, decimal? RetencionesTotal, Guid? SucursalId,
        Guid? ClienteId, decimal? TipoCambio, DateOnly? FechaContable, IReadOnlyList<FacturaLineaContrato>? Lineas,
        IReadOnlyList<RetencionesContables.RetencionContrato>? Retenciones = null,
        IReadOnlyList<ImpuestosTrasladados.ImpuestoTrasladoContrato>? Impuestos = null);

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
