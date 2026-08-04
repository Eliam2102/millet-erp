namespace Millet.Compras.Application.Lineas.AgregarLinea;

/// <summary>
/// Respuesta del comando <see cref="AgregarLineaCommand"/>. Incluye la
/// posición asignada automáticamente por el agregado para que el cliente
/// la use sin re-fetch.
/// </summary>
public sealed record AgregarLineaResponse(
    Guid Id,
    Guid RequisicionId,
    short Posicion,
    int Version);
