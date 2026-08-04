using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.Departamentos;

/// <summary>
/// DTO de respuesta para Departamento (F-Admin-PR2.3).
/// </summary>
public sealed record DepartamentoResponse(
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus,
    int Version);
