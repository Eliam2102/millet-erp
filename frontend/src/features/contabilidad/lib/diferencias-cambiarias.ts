// TES-09 · reglas de pantalla del reporte de diferencias cambiarias realizadas.

/** Mismo tope que el servidor: el periodo no puede pasar de 366 días. */
const DIAS_MAXIMOS = 366;

const diasEntre = (desde: string, hasta: string) =>
  Math.round((Date.parse(`${hasta}T00:00:00Z`) - Date.parse(`${desde}T00:00:00Z`)) / 86_400_000);

/** Motivo por el que no se puede ejecutar con esas fechas, o null si el periodo es válido. */
export function motivoPeriodoInvalido(desde: string, hasta: string): string | null {
  if (!desde || !hasta) return 'Captura las dos fechas para ejecutar el reporte.';
  if (hasta < desde) return 'La fecha final no puede ser anterior a la inicial.';
  if (diasEntre(desde, hasta) > DIAS_MAXIMOS) return 'El periodo no puede pasar de un año; acórtalo para ejecutar el reporte.';
  return null;
}
