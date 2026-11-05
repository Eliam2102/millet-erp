using Millet.SharedKernel.Application.Integration;

namespace Millet.ProductoTerminado.Application;

/// <summary>Una pieza entró a Producto Terminado. Sin estatus de A+W (D7).</summary>
public sealed record PiezaIngresadaEvent(Guid Empresa, DateTimeOffset Fecha, Guid PiezaId, Guid PedidoPtId, string NumeroPedido, int Posicion, int NumeroPieza, Guid? ProductoId, Guid SucursalId, DateTimeOffset EscaneadaAt)
    : IntegrationEvent("producto_terminado.pieza.ingresada.v1", Empresa, Fecha);

/// <summary>
/// El pedido entró a 69 (<c>Retenido</c>, con cliente, moneda y total para que CxC decida) o pasó a 70 (<c>Liberado</c>, con motivo:
/// SaldoCubierto, Reparto o DecisionCxc). Estado interno del ERP; el acuse del 70 en A+W es de U1.5.
/// </summary>
public sealed record PedidoEstadoCambiadoEvent(Guid Empresa, DateTimeOffset Fecha, Guid PedidoPtId, string NumeroPedido, string Estado, string Motivo, Guid? ClienteId, string? Moneda, decimal? Total)
    : IntegrationEvent("producto_terminado.pedido.estado-cambiado.v1", Empresa, Fecha);

/// <summary>
/// La primera hoja de salida emitida del pedido lo pasa a 71 (en ruta). Es el aviso que A+W consume (el acuse en A+W es de U1.5).
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
