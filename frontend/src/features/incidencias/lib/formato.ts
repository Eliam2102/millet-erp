import type { BadgeProps } from '@/components/ui/badge';
import { EstadoIncidencia, type IncidenciaFila } from '@/features/incidencias/api/types';
import { hoyLocalISO } from '@/lib/datetime';

type Tono = NonNullable<BadgeProps['variant']>;

/** Mapa único estado → tono + texto (DESIGN §2.1 / §4.6). */
export const ESTADO_INCIDENCIA: Record<EstadoIncidencia, { label: string; tono: Tono }> = {
  [EstadoIncidencia.Abierta]: { label: 'Abierta', tono: 'warning' },
  [EstadoIncidencia.EnAtencion]: { label: 'En atención', tono: 'info' },
  [EstadoIncidencia.Resuelta]: { label: 'Resuelta', tono: 'success' },
  [EstadoIncidencia.Cancelada]: { label: 'Cancelada', tono: 'neutral' },
};

/** Folio visible de la incidencia. */
export function folioIncidencia(numero: number): string {
  return `INC-${numero}`;
}

/** Texto de quién la abrió: «Sistema» si fue automática. */
export function origenIncidencia(i: { automatica: boolean; abiertaPorNombre: string }): string {
  return i.automatica ? 'Automática' : `Registrada por ${i.abiertaPorNombre}`;
}

/** Una ruta de reanudación sólo se sigue si es interna de la app. */
export function rutaInterna(ruta: string | null | undefined): string | null {
  return ruta && ruta.startsWith('/') && !ruta.startsWith('//') ? ruta : null;
}

/** `yyyy-MM-dd` → `dd/MM/yyyy` sin pasar por zona horaria; «—» si no hay fecha. */
export function fechaCompromisoTexto(fecha: string | null | undefined): string {
  const m = fecha ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(fecha) : null;
  return m ? `${m[3]}/${m[2]}/${m[1]}` : '—';
}

/** Compromiso vencido: sólo cuenta en incidencias pendientes. */
export function vencida(i: IncidenciaFila): boolean {
  if (!i.fechaCompromiso || i.estado === 'Resuelta' || i.estado === 'Cancelada') return false;
  return i.fechaCompromiso < hoyLocalISO();
}
