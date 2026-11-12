using Millet.SharedKernel.Application.Integration;

namespace Millet.ProductoTerminado.Application;

/// <summary>Una pieza entró a Producto Terminado. Sin estatus de A+W (D7).</summary>
public sealed record PiezaIngresadaEvent(Guid Empresa, DateTimeOffset Fecha, Guid PiezaId, Guid PedidoPtId, string NumeroPedido, int Posicion, int NumeroPieza, Guid? ProductoId, Guid SucursalId, DateTimeOffset EscaneadaAt)
    : IntegrationEvent("producto_terminado.pieza.ingresada.v1", Empresa, Fecha);

/// <summary>
/// El pedido entró a 69 (<c>Retenido</c>, con cliente, moneda y total para que CxC decida) o pasó a 70 (<c>Liberado</c>, con motivo:
/// SaldoCubierto, Reparto o DecisionCxc). Estado interno del ERP; el acuse del 70 en A+W es de U1.5.
/// </summary>
public sealed record PedidoEstadoCambiadoEvent(Guid Empresa, DateTimeOffset Fecha, Guid PedidoPtId, string NumeroPedido, string Estado, string Motivo, Guid? ClienteId, string? Moneda, decimal? Total, string? LiberadoPor = null)
    : IntegrationEvent("producto_terminado.pedido.estado-cambiado.v1", Empresa, Fecha);

/// <summary>
/// Un albarán a contra entrega (Crédito 24 horas) no se liquidó en caja el día de la salida (PT5.7): CxC levanta la alerta de cartera.
/// </summary>
public sealed record ContraEntregaNoLiquidadaEvent(Guid Empresa, DateTimeOffset Fecha, Guid PedidoPtId, string NumeroPedido, Guid ClienteId, string Moneda, decimal Total, string HojaSalidaFolio, DateTimeOffset SalidaAt)
    : IntegrationEvent("producto_terminado.contra-entrega.no-liquidada.v1", Empresa, Fecha);

/// <summary>
/// Estatus 72 para A+W (aw_retorno, script 14 v5). En A+W no se revierte (TI, 10-nov), así que se publica una sola vez, cuando un albarán del
/// pedido ya no se puede cancelar: factura timbrada (al emitir o al timbrar), entrega en ruta o liquidación cerrada. El 71 lo pone A+W con el albarán.
/// </summary>
public sealed record PedidoEnRutaEvent(Guid Empresa, DateTimeOffset Fecha, Guid PedidoPtId, string NumeroPedido, string HojaSalidaFolio, string AlbaranFolio, string? CartaPorteFolio)
    : IntegrationEvent("producto_terminado.pedido.en-ruta.v1", Empresa, Fecha);

/// <summary>Cuántas piezas de una posición se entregaron.</summary>
public sealed record PosicionEntregada(int Posicion, int Piezas);

/// <summary>
/// El chofer confirmó la entrega de un albarán (PT5.6): lo recibido por posición es lo que reconoce el costo de ventas (PCV); <c>Rechazadas</c> pasan a revisión (PT5.8).
/// <c>PedidoCompleto</c>: ya se recibieron todas las piezas del pedido.
/// </summary>
public sealed record EntregaConfirmadaEvent(Guid Empresa, DateTimeOffset Fecha, Guid PedidoPtId, string NumeroPedido, string HojaSalidaFolio, string AlbaranFolio, string Resultado, string Receptor,
    IReadOnlyList<PosicionEntregada> Entregadas, int Rechazadas, bool PedidoCompleto)
    : IntegrationEvent("producto_terminado.entrega.confirmada.v1", Empresa, Fecha);

/// <summary>El pedido está facturado y entregado: pasa a 115 (una sola vez). Es el aviso que A+W consume (el acuse en A+W es de U1.5).</summary>
public sealed record PedidoFacturadoEntregadoEvent(Guid Empresa, DateTimeOffset Fecha, Guid PedidoPtId, string NumeroPedido, string Estado)
    : IntegrationEvent("producto_terminado.pedido.facturado-entregado.v1", Empresa, Fecha);

/// <summary>
/// M-248: se corrigió una entrega confirmada antes de cerrar la liquidación. Lleva lo entregado vigente por posición (no un delta); la versión
/// anterior queda en el historial de la entrega. Informativo: el costo de ventas de PT se valúa al cerrar F1-PT-09.
/// </summary>
public sealed record EntregaCorregidaEvent(Guid Empresa, DateTimeOffset Fecha, Guid PedidoPtId, string NumeroPedido, string AlbaranFolio, int Version,
    IReadOnlyList<PosicionEntregada> Entregadas, int Rechazadas, string Motivo)
    : IntegrationEvent("producto_terminado.entrega.corregida.v1", Empresa, Fecha);
