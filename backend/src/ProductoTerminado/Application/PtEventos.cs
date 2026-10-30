using Millet.SharedKernel.Application.Integration;

namespace Millet.ProductoTerminado.Application;

/// <summary>Una pieza entró a Producto Terminado. Sin estatus de A+W (D7).</summary>
public sealed record PiezaIngresadaEvent(Guid Empresa, DateTimeOffset Fecha, Guid PiezaId, Guid PedidoPtId, string NumeroPedido, int Posicion, int NumeroPieza, Guid? ProductoId, Guid SucursalId, DateTimeOffset EscaneadaAt)
    : IntegrationEvent("producto_terminado.pieza.ingresada.v1", Empresa, Fecha);
