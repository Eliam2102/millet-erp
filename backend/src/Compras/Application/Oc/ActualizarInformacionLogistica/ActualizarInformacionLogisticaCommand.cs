using MediatR;

namespace Millet.Compras.Application.Oc.ActualizarInformacionLogistica;

/// <summary>
/// Actualiza la información logística (§4.6). Editable en Borrador,
/// Rechazada o Autorizada — la guía y el contenedor cambian durante el
/// ciclo de recepción sin re-autorización.
///
/// Si todos los campos son <c>null</c>, se limpia el snapshot.
/// </summary>
public sealed record ActualizarInformacionLogisticaCommand(
    Guid OrdenCompraId,
    string? DireccionEntrega,
    Guid? TransportistaId,
    string? TransportistaTexto,
    string? NumeroGuia,
    string? InstruccionesEnvio) : IRequest;
