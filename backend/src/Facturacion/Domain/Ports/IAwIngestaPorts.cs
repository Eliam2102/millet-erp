using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;

namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Lee la cola de solicitudes <c>aw_solicitud_pedido</c> (on-prem, esquema del
/// ERP, D18) y los datos del pedido que cada solicitud referencia. Dueño real:
/// <c>Integraciones.Aw</c> (vía Hybrid Connection); en dev es un stub que
/// devuelve vacío. Adapter real: <c>AwSolicitudesSqlReader</c> (ADR-0048 PR3).
/// </summary>
public interface IAwSolicitudesReader
{
    /// <summary>Solicitudes sin procesar, en orden por (numero_pedido, version).</summary>
    Task<IReadOnlyList<SolicitudAw>> LeerPendientesAsync(int max, CancellationToken cancellationToken);

    /// <summary>Cabecera + líneas del pedido referenciado por una solicitud.</summary>
    Task<LecturaPedidoAw> LeerDatosPedidoAsync(string numeroPedido, CancellationToken cancellationToken);
}

/// <summary>
/// Escribe de vuelta en la fila de <c>aw_solicitud_pedido</c>: claim
/// (<c>erp_pedido_id</c>), <c>estado_facturacion</c>, <c>uuid</c>, resultado.
/// Dueño real: <c>Integraciones.Aw</c>; stub que loggea
/// Adapter real: <c>AwWriteBackSqlAdapter</c> (ADR-0048 PR3/PR5). El claim nunca se borra (D18).
/// </summary>
public interface IAwWriteBackPort
{
    Task EscribirResultadoAsync(AwWriteBack writeBack, CancellationToken cancellationToken);
}

/// <summary>
/// TR-01: versión del pedido A+W que el ERP ya aplicó. Integraciones.Aw la usa para
/// reemplazar una fila de <c>aw_retorno</c> desfasada (SRC-114 3a) sin leer tablas de Facturación.
/// </summary>
public interface IAwVersionPedidoReadPort
{
    Task<long?> VersionAplicadaAsync(string numeroPedido, CancellationToken cancellationToken);
}

/// <summary>Pagos acumulados de una factura A+W por forma de pago SAT + moneda (retorno tipo 4). Importe &gt; 0.</summary>
public sealed record PagoGrupoAw(string FormaPagoSat, string Moneda, decimal Importe);

/// <param name="Folio">Folio del comprobante (el mismo del retorno tipo 3).</param>
/// <param name="EsFacturaFinal">true = FacturaVenta; false = FacturaAnticipo.</param>
public sealed record PagosFacturaAw(string NumeroPedidoAw, string Folio, bool EsFacturaFinal, IReadOnlyList<PagoGrupoAw> Grupos);

/// <summary>
/// Pagos aplicados (acumulado) a las facturas A+W, para que Integraciones.Aw escriba filas tipo 4 de
/// <c>aw_retorno</c> sin leer tablas de Facturación. Lectura pura.
/// </summary>
public interface IAwPagosReadPort
{
    /// <summary>Factura/anticipo → ella misma; REPP → cada factura que paga. Facturas no A+W o canceladas se omiten.</summary>
    Task<IReadOnlyList<PagosFacturaAw>> PorComprobanteAsync(Guid comprobanteId, CancellationToken cancellationToken);

    Task<PagosFacturaAw?> PorFolioAsync(string numeroPedidoAw, string folio, CancellationToken cancellationToken);
}

/// <summary>
/// 86e3wjt5r: tabla puente de notas de crédito de A+W (<c>aw_solicitud_nota_credito</c>, script 17, en
/// MILLET_INTEGRACION). A+W (un job, por confirmar con TI) escribe la nota; el ERP lee las pendientes y
/// escribe el acuse. Dueño real: <c>Integraciones.Aw</c>; sin canal configurado, un stub que devuelve vacío.
/// </summary>
public interface IAwNotasCreditoPort
{
    /// <summary>Notas sin acuse, en orden por (número de nota, versión).</summary>
    Task<IReadOnlyList<SolicitudNotaCreditoAw>> LeerNotasPendientesAsync(int max, CancellationToken cancellationToken);

    Task EscribirAcuseAsync(AcuseNotaCreditoAw acuse, CancellationToken cancellationToken);
}

/// <summary>Saldo por cobrar de una factura (dueño: CuentasPorCobrar). Null = la cartera aún no la proyecta.</summary>
public interface ISaldoFacturaReadPort
{
    Task<decimal?> ObtenerSaldoAsync(Guid facturaVentaId, CancellationToken cancellationToken);
}

/// <param name="Motivo">«Bonificacion» o «Devolucion».</param>
/// <param name="Importe">Total de la nota con IVA.</param>
/// <param name="FacturaFolio">Folio de la factura origen; vacío = la del pedido.</param>
/// <param name="Vendedor">Opcional (aditivo): vendedor de la nota en A+W (KA_FIRMA_MITARB). Solo informativo.</param>
/// <param name="EsRanura">Opcional (aditivo): el importe es el de la ranura del pedido, que el ERP ya documenta con su propia NC de ranura al timbrar la factura; no se crea otra.</param>
public sealed record SolicitudNotaCreditoAw(
    Guid SolicitudId,
    string NumeroNota,
    long Version,
    string NumeroPedido,
    string? FacturaFolio,
    string ClienteRef,
    string Motivo,
    decimal Importe,
    string? Descripcion,
    DateTimeOffset CreadaAt,
    string? Vendedor = null,
    bool EsRanura = false);

/// <summary>
/// Tolerancia de redondeo entre la nota de ranura de A+W y la ranura del pedido (o la NC de ranura que emite el ERP).
/// A+W calcula la ranura con IVA y su redondeo difiere hasta por centavos o algunos decimales: en 60 días de MILMAIN
/// (10-nov-2026) 1,017 notas cuadran a 0.02 y otras 10 a 0.03–0.86. Hasta $1.00 se da por la misma ranura; arriba se
/// trata como bonificación aparte (por autorizar).
/// </summary>
public static class RanuraAw
{
    public const decimal Tolerancia = 1.00m;
}

public sealed record AcuseNotaCreditoAw(
    Guid SolicitudId,
    Guid? ErpNotaCreditoId,
    string? Estado,
    ResultadoSolicitudAw Resultado,
    string? Motivo);

public sealed record SolicitudAw(
    Guid SolicitudId,
    string NumeroPedido,
    OperacionAw Operacion,
    long Version,
    DateTimeOffset CreadaAt);

/// <param name="CanalVenta">
/// Id del canal en el catálogo <c>compartido.canales_venta</c> (FAC-ING-PR2).
/// Lo resuelve el reader por <c>clave_aw</c> (GRUPPE crudo de la vista) vía
/// <c>ICanalVentaPorClaveAwResolver</c>, igual que la sucursal.
/// </param>
/// <param name="Ranura">
/// Descuento "ranura" a nivel cabecera del pedido A+W
/// (<c>BW_AUFTR_KOPF.KO_FALZ</c>, RANURA-PR1). BRUTO (con IVA), como los
/// demás importes de las vistas. NO se resta en la factura: se documenta
/// con una nota de crédito (relación 01) y la caja cobra
/// <c>total − NC acreditadas</c> ([Decisión 13-K]). <c>null</c>/0 = sin
/// ranura. Extensión aditiva al contrato, mismo trato que TasaIva.
/// </param>
/// <param name="MetodoPagoOrigen">
/// Método de pago SAT que manda A+W (<c>metodo_pago</c> de la vista,
/// derivado de la condición: CONTADO→PUE, resto→PPD; doc 04 §3). Informativo:
/// alimenta <c>PedidoFacturable.EsCredito</c> (86e3tfdw2). Extensión aditiva.
/// </param>
/// <param name="CondicionPagoOrigen">
/// Condición de pago del pedido en A+W (<c>condicion_pago</c>, catálogo
/// KA_ZAHLBED; vacío → CONTADO). Distinta de CONTADO = pedido a crédito
/// (86e3tfdw2). Extensión aditiva.
/// </param>
public sealed record DatosPedidoAw(
    string NumeroPedido,
    Guid SucursalId,
    string ClienteRef,
    string ClienteNombre,
    short CanalVenta,
    ComportamientoFiscal ComportamientoFiscal,
    string Moneda,
    long? ObraId,
    string? ObraNombre,
    string? Comentarios,
    string? EstadoOrigen,
    IReadOnlyList<LineaPedidoAw> Lineas,
    string PayloadCrudo,
    decimal? Ranura = null,
    string? MetodoPagoOrigen = null,
    string? CondicionPagoOrigen = null);

/// <summary>
/// Resultado de leer los datos de un pedido: <see cref="Datos"/> completos, o
/// el motivo por el que no están disponibles. <see cref="EsperaConfiguracion"/>
/// separa las causas corregibles en catálogos del ERP (clave_aw faltante en
/// canal/sucursal, regla clase→comportamiento pendiente, cliente no
/// provisionable) — que la ingesta POSPONE y reintenta sola en cada tick —
/// de los datos defectuosos del origen (sin cabecera, sin líneas, totales que
/// no cuadran), que se RECHAZAN a A+W con versión nueva obligatoria.
/// </summary>
public sealed record LecturaPedidoAw(
    DatosPedidoAw? Datos,
    MotivoExcepcion Motivo,
    string? Detalle,
    bool EsperaConfiguracion,
    DiferenciaCampoIngesta? Diferencia = null)
{
    public static LecturaPedidoAw Ok(DatosPedidoAw datos) => new(datos, MotivoExcepcion.Otro, null, false);
    public static LecturaPedidoAw DatosInvalidos(MotivoExcepcion motivo, string detalle, DiferenciaCampoIngesta? diferencia = null) =>
        new(null, motivo, detalle, false, diferencia);
    public static LecturaPedidoAw EsperaConfig(MotivoExcepcion motivo, string detalle) => new(null, motivo, detalle, true);
}

/// <param name="Precio">
/// Precio unitario. Cuando <paramref name="TasaIva"/> viene informada, el
/// reader ya lo convirtió a NETO (sin IVA) desde el bruto de A+W
/// (FAC-DET-PR2); con <paramref name="TasaIva"/> <c>null</c> viaja tal cual
/// lo entrega la vista (comportamiento previo intacto).
/// </param>
/// <param name="BomJson">
/// Componentes/BOM de la posición (informativo, ADR-0048 D8): arreglo JSON
/// serializado por el reader desde <c>vw_erp_pedido_componente</c>
/// (BW_AUFTR_STKL). <c>null</c> si el origen no aporta BOM.
/// </param>
/// <param name="TasaIva">
/// Tasa de IVA del documento A+W (fracción, p.ej. 0.16 — FAC-DET-PR2). En
/// A+W el IVA es a nivel documento (BW_AUFTR_KOPF.FI_MWST1 → KA_MWST) y se
/// transfiere a todas las posiciones. <c>null</c> = la vista no aporta tasa.
/// Extensión aditiva al contrato F3, mismo trato que BomJson.
/// </param>
public sealed record LineaPedidoAw(
    string ProductoRef,
    string Descripcion,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    decimal Cantidad,
    decimal Precio,
    decimal Descuento,
    bool RequierePedimento,
    string? BomJson = null,
    decimal? TasaIva = null);

public sealed record AwWriteBack(
    Guid SolicitudId,
    string NumeroPedido,
    Guid? ErpPedidoId,
    string? EstadoFacturacion,
    string? Uuid,
    ResultadoSolicitudAw Resultado,
    string? Motivo);
