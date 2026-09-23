using Millet.Catalogos.Domain;

namespace Millet.Identidad.Application.UsuarioSucursales;

/// <summary>
/// DTO de respuesta para una asignación Usuario ↔ Sucursal (F1-ADM-01
/// Fase 2). Trae el email/nombre del usuario ya joineados para evitar
/// un segundo fetch desde la UI — mismo criterio que
/// <c>SucursalDepartamentoResponse</c>.
/// </summary>
public sealed record UsuarioSucursalResponse(
    Guid SucursalId,
    Guid UsuarioId,
    string UsuarioEmail,
    string UsuarioNombre,
    EstatusCatalogo Estatus,
    int Version);
