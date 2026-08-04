using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.Sucursales;

/// <summary>
/// DTO de respuesta para Sucursal (F-Admin-PR2.3).
/// </summary>
public sealed record SucursalResponse(
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus,
    int Version,
    string? ClaveAw,
    string ZonaHoraria);
