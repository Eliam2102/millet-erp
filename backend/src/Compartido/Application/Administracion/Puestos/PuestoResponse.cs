using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.Puestos;

/// <summary>DTO de respuesta para Puesto (ADM-PR1, F1-ADM-01.4).</summary>
public sealed record PuestoResponse(
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus,
    int Version,
    Guid? RolSugeridoId = null,
    string? RolSugeridoNombre = null,
    Guid? DepartamentoId = null,
    string? DepartamentoNombre = null);
