using MediatR;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Ingesta.ImportarPedidoPlantaPintura;

/// <summary>
/// Ingesta una orden facturable de Planta Pintura (§12.1, §7.1, F10-PR2). A
/// diferencia de A+W, <b>exige master preexistente</b>: si el cliente o algún
/// artículo no existe en el ERP, genera una excepción (no auto-provisiona). El
/// <c>EmpresaId</c> viaja en el comando porque el worker corre con
/// <c>empresaContext.Bypass()</c>.
/// </summary>
public sealed record ImportarPedidoPlantaPinturaCommand(
    Guid EmpresaId,
    PedidoPlantaPintura Pedido) : IRequest<ImportarPedidoPlantaPinturaResponse>;

public sealed record ImportarPedidoPlantaPinturaResponse(
    ResultadoSolicitudAw Resultado,
    Guid? PedidoFacturableId,
    MotivoExcepcion? MotivoExcepcion);
