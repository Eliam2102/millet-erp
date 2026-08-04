namespace Millet.SharedKernel.Application.Settings;

/// <summary>
/// Respuesta del endpoint <c>GET /api/v1/{modulo}/settings/schema</c>.
/// La lista <see cref="Items"/> ya está filtrada por los permisos del
/// usuario actual: items cuyo <see cref="SettingItem.PermisoLeer"/> el
/// usuario no posee se omiten silenciosamente.
/// </summary>
public sealed record SettingsSchemaResponse(IReadOnlyList<SettingItem> Items);
