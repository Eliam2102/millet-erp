using Millet.Compras.Domain;

namespace Millet.Compras.Application.CrearRequisicion;

/// <summary>
/// Respuesta de <see cref="CrearRequisicionCommand"/>. Incluye los
/// campos críticos para que el cliente confirme la creación y construya
/// el siguiente request (ETag para edición, redirect a GET).
/// </summary>
public sealed record CrearRequisicionResponse(
    Guid Id,
    string Folio,
    short FolioAnio,
    EstadoRequisicion Estado,
    int Version);
