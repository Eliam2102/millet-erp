/**
 * Tipos del schema declarativo de settings expuesto por
 * <c>GET /api/v1/{modulo}/settings/schema</c> (ADR-0034).
 *
 * <para><b>Serialización de enums</b>: el backend (System.Text.Json) NO
 * tiene <c>JsonStringEnumConverter</c> registrado globalmente, así que
 * <see cref="TipoSetting"/> y <see cref="SettingDisplayMode"/> viajan
 * como números enteros. Los aliases nominales (<c>Booleano</c>,
 * <c>Auto</c>, etc.) están definidos como constantes y como string-keys
 * del tipo para facilitar el switch en la UI sin perder el contrato
 * numérico.</para>
 */

/**
 * Mirror numérico del enum del backend
 * <c>Millet.SharedKernel.Application.Settings.TipoSetting</c>.
 *
 * <list type="bullet">
 *   <item><c>0</c> — Booleano (Switch).</item>
 *   <item><c>1</c> — Entero (Input number).</item>
 *   <item><c>2</c> — Numérico decimal (Input number).</item>
 *   <item><c>3</c> — Texto (Input text).</item>
 *   <item><c>4</c> — Lista / enum (Select).</item>
 *   <item><c>5</c> — Fecha (DatePicker).</item>
 * </list>
 */
export const TipoSetting = {
  Booleano: 0,
  Entero: 1,
  Numerico: 2,
  Texto: 3,
  Lista: 4,
  Fecha: 5,
} as const;
export type TipoSetting = (typeof TipoSetting)[keyof typeof TipoSetting];

/**
 * Mirror numérico del enum del backend
 * <c>Millet.SharedKernel.Application.Settings.DisplayMode</c>.
 *
 * <list type="bullet">
 *   <item><c>0</c> — Auto: el form se renderiza automáticamente desde
 *         el schema (<c>&lt;AutoSettingsForm/&gt;</c>).</item>
 *   <item><c>1</c> — Custom: la card linkea a <c>rutaCustom</c> (UI
 *         dedicada por el módulo). El endpoint genérico PATCH sigue
 *         funcionando para automation.</item>
 * </list>
 */
export const SettingDisplayMode = {
  Auto: 0,
  Custom: 1,
} as const;
export type SettingDisplayMode =
  (typeof SettingDisplayMode)[keyof typeof SettingDisplayMode];

/**
 * Validaciones declarativas opcionales del setting. El backend usa
 * <c>Min/Max</c> para Entero/Numerico, <c>Pattern</c> para Texto y
 * <c>Opciones</c> para Lista.
 */
export interface ValidacionSetting {
  min?: number | null;
  max?: number | null;
  pattern?: string | null;
  opciones?: readonly string[] | null;
}

/** Descriptor de un setting individual del módulo. */
export interface SettingItem {
  clave: string;
  etiqueta: string;
  descripcion: string;
  tipo: TipoSetting;
  default: unknown;
  valor: unknown;
  validacion?: ValidacionSetting | null;
  permisoLeer: string;
  permisoEditar: string;
  mostrar: SettingDisplayMode;
  rutaCustom?: string | null;
  alertaCambio?: string | null;
}

/** Respuesta del endpoint <c>GET /api/v1/{modulo}/settings/schema</c>. */
export interface SettingsSchemaResponse {
  items: SettingItem[];
}
