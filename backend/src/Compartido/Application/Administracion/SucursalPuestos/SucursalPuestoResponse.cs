using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// DTO de respuesta para una asignación Sucursal ↔ Puesto ↔ Departamento.
/// Trae los datos del puesto (clave + nombre) ya joineados para evitar un
/// segundo fetch desde la UI. Análogo exacto de
/// <c>SucursalDepartamentoResponse</c> (F1-ADM-01 Fase 2).
///
/// <para>
/// F1-ADM-01.4 reabierta (puesto en varios departamentos de la
/// sucursal): <see cref="RolSugeridoId"/> es la excepción opcional de
/// <em>esta</em> asignación puntual (sucursal + puesto + departamento);
/// <see cref="RolSugeridoEfectivoId"/> es el rol que realmente se
/// sugeriría en el wizard —
/// <c>RolSugeridoId (asignación) ?? RolSugeridoId (puesto)</c>. Sigue
/// siendo sólo sugerencia (01-04): el rol se asigna explícito.
/// </para>
/// </summary>
public sealed record SucursalPuestoResponse(
    Guid SucursalId,
    Guid PuestoId,
    string PuestoClave,
    string PuestoNombre,
    Guid DepartamentoId,
    string? DepartamentoNombre,
    EstatusCatalogo Estatus,
    int Version,
    Guid? RolSugeridoId = null,
    Guid? RolSugeridoEfectivoId = null);
