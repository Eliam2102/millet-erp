namespace Millet.SharedKernel.Application.Settings;

/// <summary>
/// Descriptor declarativo de un setting expuesto por un módulo. El
/// endpoint genérico <c>GET /api/v1/{modulo}/settings/schema</c> retorna
/// una lista de estos items filtrados por <see cref="PermisoLeer"/> del
/// usuario actual; el frontend los renderiza si <see cref="Mostrar"/>
/// es <see cref="DisplayMode.Auto"/> o linkea a
/// <see cref="RutaCustom"/> si es <see cref="DisplayMode.Custom"/>.
/// Ver ADR-0034 §SettingsSchema.
/// </summary>
/// <param name="Clave">
/// Identificador del setting dentro del módulo. Convención PascalCase
/// alineada con la propiedad del aggregate (ej. <c>AutoGenerarOcAlAutorizar</c>).
/// Es el segmento <c>{clave}</c> del endpoint PATCH.
/// </param>
/// <param name="Etiqueta">Texto corto para el label del control.</param>
/// <param name="Descripcion">Texto largo para tooltip / help text.</param>
/// <param name="Tipo">Tipo de dato — determina el control UI y la validación.</param>
/// <param name="Default">Valor por defecto cuando la empresa no lo ha personalizado.</param>
/// <param name="Valor">Valor actual para la empresa activa.</param>
/// <param name="Validacion">Validaciones declarativas (opcional).</param>
/// <param name="PermisoLeer">Permiso canónico requerido para leer este item.</param>
/// <param name="PermisoEditar">Permiso canónico requerido para PATCHearlo.</param>
/// <param name="Mostrar">Modo de visualización en <c>/admin</c>.</param>
/// <param name="RutaCustom">
/// Si <see cref="Mostrar"/> = <see cref="DisplayMode.Custom"/>, ruta del
/// frontend a la UI dedicada del setting (ej. <c>/compras/configuracion</c>).
/// Opcional para <see cref="DisplayMode.Auto"/>.
/// </param>
/// <param name="AlertaCambio">
/// Texto opcional para mostrar en un <c>Dialog</c> de confirmación
/// antes de aplicar el cambio (cuando tiene efectos non-trivial).
/// </param>
public sealed record SettingItem(
    string Clave,
    string Etiqueta,
    string Descripcion,
    TipoSetting Tipo,
    object? Default,
    object? Valor,
    ValidacionSetting? Validacion,
    string PermisoLeer,
    string PermisoEditar,
    DisplayMode Mostrar,
    string? RutaCustom = null,
    string? AlertaCambio = null);
