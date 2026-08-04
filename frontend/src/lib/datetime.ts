import { format, formatDistanceToNow, parseISO } from 'date-fns';
import { fromZonedTime, toZonedTime } from 'date-fns-tz';
import { es } from 'date-fns/locale';

/**
 * Zona horaria del sistema. TODAS las conversiones UTC → local pasan por
 * aquí. El backend almacena timestamps en UTC; el frontend nunca muestra
 * UTC al usuario. Ver ADR-0013.
 */
export const TIMEZONE = 'America/Mexico_City' as const;

/** Formatea un instante UTC como fecha-hora local: dd/MM/yyyy HH:mm. */
export function formatDateTime(utc: string | Date): string {
  const date = typeof utc === 'string' ? parseISO(utc) : utc;
  const local = toZonedTime(date, TIMEZONE);
  return format(local, 'dd/MM/yyyy HH:mm');
}

/** Formatea un instante UTC como fecha local: dd/MM/yyyy. */
export function formatDate(utc: string | Date): string {
  const date = typeof utc === 'string' ? parseISO(utc) : utc;
  const local = toZonedTime(date, TIMEZONE);
  return format(local, 'dd/MM/yyyy');
}

/** Formato largo en español: "2 de mayo de 2026". */
export function formatDateLong(utc: string | Date): string {
  const date = typeof utc === 'string' ? parseISO(utc) : utc;
  const local = toZonedTime(date, TIMEZONE);
  return format(local, "d 'de' MMMM 'de' yyyy", { locale: es });
}

/** Formato con segundos para logs/auditoría: dd/MM/yyyy HH:mm:ss. */
export function formatDateTimeWithSeconds(utc: string | Date): string {
  const date = typeof utc === 'string' ? parseISO(utc) : utc;
  const local = toZonedTime(date, TIMEZONE);
  return format(local, 'dd/MM/yyyy HH:mm:ss');
}

/** Formato relativo en español: "hace 5 minutos", "hace 2 horas". */
export function formatRelative(utc: string | Date): string {
  const date = typeof utc === 'string' ? parseISO(utc) : utc;
  return formatDistanceToNow(date, { addSuffix: true, locale: es });
}

/**
 * Convierte un input local del usuario (interpretado en TIMEZONE) a un
 * Date UTC equivalente, listo para enviar al backend en formato ISO 8601.
 * Ejemplo: usuario captura "02/05/2026 14:30" → backend recibe
 * "2026-05-02T20:30:00.000Z" (México está en UTC-6).
 */
export function parseLocalToUtc(localISOString: string): Date {
  const localDate = parseISO(localISOString);
  return fromZonedTime(localDate, TIMEZONE);
}

/**
 * Fecha de calendario de hoy en TIMEZONE como `YYYY-MM-DD`. Para defaults
 * de campos `DateOnly` (ADR-0040). NO usar
 * `new Date().toISOString().split('T')[0]`: daría la fecha UTC y
 * adelantaría el día después de las 18:00 hora local de México.
 */
export function hoyLocalISO(): string {
  return format(toZonedTime(new Date(), TIMEZONE), 'yyyy-MM-dd');
}

/**
 * Parsea una fecha de calendario `YYYY-MM-DD` (DateOnly del backend,
 * ADR-0040) a un `Date` en medianoche LOCAL, sin el shift de zona que
 * provoca `new Date('2026-06-07')` (que la interpreta como medianoche UTC
 * y en México cae al día anterior). Si el string trae hora (ISO completo),
 * cae a `parseISO`.
 */
export function parseDateOnlyLocal(value: string): Date {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (m) return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return parseISO(value);
}
