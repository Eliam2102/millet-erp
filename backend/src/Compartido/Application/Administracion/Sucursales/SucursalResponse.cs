using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.Sucursales;

/// <summary>
/// DTO de respuesta para Sucursal (F-Admin-PR2.3 / Fase 2).
/// </summary>
public sealed record SucursalResponse(
    Guid Id,
    string Clave,
    string Nombre,
    TipoSucursal Tipo,
    EstatusCatalogo Estatus,
    int Version,
    string? ClaveAw,
    string ZonaHoraria);
