import { startOfDay, endOfDay } from 'date-fns';

/**
 * Día deshabilitado cuando cae fuera del rango <c>[minDate, maxDate]</c>,
 * comparado a granularidad de <b>día</b> (el <c>&lt;DatePickerField&gt;</c> es
 * DateOnly): se trunca <c>minDate</c> a inicio-de-día y <c>maxDate</c> a
 * fin-de-día para que un límite con hora (p.ej. <c>new Date()</c> = ahora) no
 * excluya el propio día. <c>react-day-picker</c> pasa cada celda a medianoche
 * local; el off-by-one era comparar esa medianoche contra "ahora con hora".
 *
 * <para>Vive en su propio archivo (no en <c>DatePickerField.tsx</c>) para no
 * romper Fast Refresh — <c>react-refresh/only-export-components</c> — y para
 * poder testearlo como función pura.</para>
 */
export function esDiaDeshabilitado(
  d: Date,
  minDate?: Date,
  maxDate?: Date,
): boolean {
  if (minDate && d < startOfDay(minDate)) return true;
  if (maxDate && d > endOfDay(maxDate)) return true;
  return false;
}
