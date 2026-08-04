using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.CanalesVenta;

/// <summary>
/// DTO de respuesta para CanalVenta (FAC-ING-PR2). Mismo shape que
/// <c>SucursalResponse</c>: catálogo administrable con clave A+W opcional.
/// </summary>
public sealed record CanalVentaResponse(
    short Id,
    string Nombre,
    EstatusCatalogo Estatus,
    int Version,
    string? ClaveAw);
