using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.Puestos;

/// <summary>DTO de respuesta para Puesto (ADM-PR1).</summary>
public sealed record PuestoResponse(
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus,
    int Version);
