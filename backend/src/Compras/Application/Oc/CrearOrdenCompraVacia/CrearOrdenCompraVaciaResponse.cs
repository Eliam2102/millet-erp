using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Oc.CrearOrdenCompraVacia;

/// <summary>
/// Respuesta de <see cref="CrearOrdenCompraVaciaCommand"/>. Incluye lo
/// crítico para que el cliente confirme la creación y prepare el siguiente
/// request (ETag para edición vía <see cref="Version"/>, redirect a GET).
/// </summary>
public sealed record CrearOrdenCompraVaciaResponse(
    Guid Id,
    string Folio,
    short FolioAnio,
    EstadoOrdenCompra Estado,
    int Version);
