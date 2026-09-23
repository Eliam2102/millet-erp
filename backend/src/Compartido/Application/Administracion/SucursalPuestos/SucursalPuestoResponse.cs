using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// DTO de respuesta para una asignación Sucursal ↔ Puesto. Trae los
/// datos del puesto (clave + nombre) ya joineados para evitar un
/// segundo fetch desde la UI. Análogo exacto de
/// <c>SucursalDepartamentoResponse</c> (F1-ADM-01 Fase 2).
/// </summary>
public sealed record SucursalPuestoResponse(
    Guid SucursalId,
    Guid PuestoId,
    string PuestoClave,
    string PuestoNombre,
    Guid DepartamentoId,
    string? DepartamentoNombre,
    EstatusCatalogo Estatus,
    int Version);
