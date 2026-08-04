using System.Text.Json;

namespace Millet.SharedKernel.Application.Settings;

/// <summary>
/// Contrato que cada módulo de negocio implementa para exponer sus
/// settings vía el endpoint genérico
/// <c>GET/PATCH /api/v1/{modulo}/settings</c>. Una implementación por
/// módulo (Compras, Facturación, CxP, ...); el endpoint resuelve la
/// instancia correcta filtrando por <see cref="Modulo"/>.
/// Ver ADR-0034.
/// </summary>
public interface ISettingsSchemaProvider
{
    /// <summary>
    /// Identificador del módulo en minúsculas, coincide con el segmento
    /// <c>{modulo}</c> del URL (ej. <c>"compras"</c>, <c>"facturacion"</c>).
    /// </summary>
    string Modulo { get; }

    /// <summary>
    /// Retorna el schema completo de settings del módulo para la
    /// empresa activa. El endpoint filtra por permisos del usuario
    /// antes de responder — el provider no necesita preocuparse por
    /// autorización aquí.
    /// </summary>
    Task<IReadOnlyList<SettingItem>> ObtenerSchemaAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Aplica un cambio a un setting individual. El endpoint ya
    /// validó tipo (<see cref="SettingItem.Tipo"/>) y validaciones
    /// declarativas (<see cref="SettingItem.Validacion"/>) antes de
    /// invocar este método; el provider solo persiste el cambio
    /// (típicamente vía MediatR a su command existente).
    ///
    /// <para>
    /// Retorna el <see cref="SettingItem"/> actualizado para que el
    /// caller pueda devolverlo al cliente (incluye el nuevo
    /// <see cref="SettingItem.Valor"/>).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si <paramref name="clave"/> no existe en el schema del módulo.
    /// </exception>
    Task<SettingItem> AplicarPatchAsync(
        string clave,
        JsonElement valor,
        CancellationToken cancellationToken);
}
