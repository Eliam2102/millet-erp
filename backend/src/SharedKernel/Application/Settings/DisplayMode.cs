namespace Millet.SharedKernel.Application.Settings;

/// <summary>
/// Modo de visualización del setting en el área de administración
/// <c>/admin/&lt;modulo&gt;/settings</c>:
///
/// <list type="bullet">
///   <item><c>Auto</c> — el form se renderiza automáticamente desde el
///         schema usando shadcn/ui (Switch/Input/Select/DatePicker
///         según <see cref="TipoSetting"/>). PATCH también va al endpoint
///         genérico.</item>
///   <item><c>Custom</c> — el card del módulo linkea a
///         <see cref="SettingItem.RutaCustom"/> que provee UI dedicada
///         (típicamente porque el setting tiene efectos cross-cutting,
///         como <c>AutoGenerarOcAlAutorizar</c> en Compras). El endpoint
///         genérico de PATCH sigue funcionando para automation/scripts.</item>
/// </list>
/// Ver ADR-0034.
/// </summary>
public enum DisplayMode
{
    Auto = 0,
    Custom = 1,
}
