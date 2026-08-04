using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.SucursalDepartamentos;

/// <summary>
/// DTO de respuesta para una asignación Sucursal ↔ Departamento.
/// Trae los datos del departamento (clave + nombre) ya joineados para
/// evitar un segundo fetch desde la UI.
/// </summary>
public sealed record SucursalDepartamentoResponse(
    Guid SucursalId,
    Guid DepartamentoId,
    string DepartamentoClave,
    string DepartamentoNombre,
    EstatusCatalogo Estatus,
    int Version);
