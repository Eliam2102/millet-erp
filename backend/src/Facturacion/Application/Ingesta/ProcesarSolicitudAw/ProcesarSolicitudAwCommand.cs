using MediatR;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Ingesta.ProcesarSolicitudAw;

/// <summary>
/// Procesa una solicitud de la cola A+W (Alta/Modificación/Cancelación) según la
/// matriz operación×estado (§12.1, D18, D20). El worker la invoca por cada
/// solicitud pendiente; el handler aplica la idempotencia (doble candado vía
/// <c>ingesta_control</c>), muta el <c>PedidoFacturable</c> y escribe de vuelta.
/// </summary>
public sealed record ProcesarSolicitudAwCommand(Guid EmpresaId, SolicitudAw Solicitud)
    : IRequest<ProcesarSolicitudAwResponse>;

public sealed record ProcesarSolicitudAwResponse(
    ResultadoSolicitudAw Resultado,
    Guid? PedidoFacturableId,
    string? Motivo);
