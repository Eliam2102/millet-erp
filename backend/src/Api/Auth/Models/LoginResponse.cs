using Millet.Compras.Application.Settings;

namespace Millet.Api.Auth.Models;

/// <summary>Datos del usuario autenticado para el frontend.</summary>
public sealed record UsuarioInfo(Guid Id, string Email, string Nombre);

/// <summary>
/// Una empresa accesible para el usuario. <see cref="EsLaActual"/> indica si
/// es la que quedó como <c>current_empresa_id</c> en el JWT.
/// </summary>
public sealed record EmpresaInfo(Guid Id, string Rfc, string RazonSocial, bool EsLaActual);

/// <summary>
/// Respuesta de <c>POST /api/auth/sesion</c> y <c>POST /api/dev/fake-login</c>.
/// El frontend almacena <see cref="AccessToken"/> en memoria y lo manda en
/// header <c>Authorization: Bearer</c> en cada request. <see cref="ExpiresAt"/>
/// le dice cuándo programar refresh silent (vía MSAL).
///
/// Si <see cref="Empresas"/> tiene más de un elemento y ninguna está marcada
/// como actual, el frontend debe mostrar selector de empresa antes de
/// continuar (ver <c>POST /api/auth/cambiar-empresa</c>, PR 4).
///
/// <see cref="Permisos"/> contiene los códigos de permiso del usuario en la
/// empresa actualmente seleccionada (ej. <c>infra.health.leer</c>). Lista
/// vacía si el usuario no tiene asignaciones o no hay empresa seleccionada.
/// El frontend usa esto para condicional UI (ej. ocultar botones a los que
/// el usuario no tiene acceso vía <c>useHasPermission()</c>).
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UsuarioInfo Usuario,
    IReadOnlyList<EmpresaInfo> Empresas,
    IReadOnlyList<string> Permisos,
    ComprasSettingsResponse? ComprasSettings);
