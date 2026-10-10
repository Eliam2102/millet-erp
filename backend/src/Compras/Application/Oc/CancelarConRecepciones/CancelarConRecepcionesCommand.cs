using MediatR;

namespace Millet.Compras.Application.Oc.CancelarConRecepciones;

/// <summary>Firma N1 de la solicitud de cancelación con recepciones.</summary>
public sealed record CancelarConRecepcionesCommand(
    Guid OrdenCompraId,
    Guid MotivoCancelacionId,
    string? MotivoCancelacionTexto) : IRequest;
