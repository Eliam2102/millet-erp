using Millet.Compras.Application.Settings;

namespace Millet.Api.Auth.Models;

/// <summary>
/// Respuesta de <c>GET /api/auth/me</c>. Refleja los claims del JWT actual
/// más los permisos efectivos del usuario en la empresa seleccionada
/// (cargados desde <c>IPermissionCache</c>; en miss desde la BD).
///
/// El frontend lo llama tras login + en cada cambio de empresa para
/// reconciliar UI condicional con el estado del backend (los permisos
/// pueden haber cambiado si un admin asignó/revocó roles).
///
/// <para>
/// <see cref="ComprasSettings"/> viaja en el payload para que el FE no
/// tenga que hacer un fetch extra al inicio de la sesión. Es
/// <c>null</c> si no hay empresa seleccionada.
/// </para>
/// </summary>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    string Nombre,
    Guid? CurrentEmpresaId,
    Guid? DepartamentoId,
    IReadOnlyList<string> Permisos,
    ComprasSettingsResponse? ComprasSettings);
