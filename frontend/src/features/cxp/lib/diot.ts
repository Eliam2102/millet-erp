import { hoyLocalISO } from '@/lib/datetime';

/** Mes anterior al de hoy en formato `YYYY-MM`: la DIOT se presenta el mes siguiente. */
export function mesAnteriorIso(hoy: string = hoyLocalISO()): string {
  const [anio, mes] = hoy.split('-').map(Number);
  return mes === 1 ? `${anio - 1}-12` : `${anio}-${String(mes - 1).padStart(2, '0')}`;
}
