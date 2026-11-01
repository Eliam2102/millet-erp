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
