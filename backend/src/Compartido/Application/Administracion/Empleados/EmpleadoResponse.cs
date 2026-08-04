using Millet.Catalogos.Domain;

namespace Millet.Administracion.Application.Empleados;

/// <summary>DTO de respuesta para Empleado (ADM-PR1).</summary>
public sealed record EmpleadoResponse(
    Guid Id,
    Guid EmpresaId,
    string Clave,
    string Nombre,
    string? Email,
    Guid? PuestoId,
    Guid? JefeDirectoId,
    Guid? SucursalId,
    Guid? DepartamentoId,
    Guid? UsuarioId,
    string? CodigoNomina,
    EstatusCatalogo Estatus,
    int Version);
