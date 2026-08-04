using System.Text.Json;

namespace Millet.SharedKernel.Application.Settings;

/// <summary>
/// Body del endpoint <c>PATCH /api/v1/{modulo}/settings/{clave}</c>.
/// <see cref="Valor"/> es <see cref="JsonElement"/> para que el
/// endpoint pueda validar el tipo declarado en
/// <see cref="SettingItem.Tipo"/> antes de delegar al provider.
/// </summary>
public sealed record AplicarSettingPatchRequest(JsonElement Valor);
