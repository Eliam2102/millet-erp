import { differenceInCalendarDays, isValid, parseISO } from 'date-fns';
import { toZonedTime } from 'date-fns-tz';
import { TIMEZONE } from '@/lib/datetime';
import type { ConteoInicio } from './api/useConteosInicio';

// Umbrales operativos de Inicio, no estados de dominio ni SLA de Millet.
// >3 días: atención; >7: crítico. Carga de trabajo: >10 / >25 registros,
// con o sin fecha. Se toma el nivel más alto de las señales disponibles.
// Partidas atrasadas: cualquiera pide atención; >10 pide revisión crítica.
// No hay montos ni bloqueo interárea en estos DTO: no se infieren esos factores.
export const UMBRALES_INICIO = {
  diasAtencion: 3,
  diasCritico: 7,
  cantidadAtencion: 10,
  cantidadCritico: 25,
  atrasadasCritico: 10,
} as const;
export type NivelInicio = 'normal' | 'atencion' | 'critico';
export interface PrioridadInicio {
  nivel: NivelInicio;
  dias?: number;
  total: number;
  motivo: string;
  antiguedad: string;
  urgente: boolean;
}

/** DateOnly conserva su calendario; instantes se convierten a la zona del ERP. */
export function antiguedadDias(fecha: string | undefined, ahora: Date): number | undefined {
  if (!fecha) return undefined;
  const date = parseISO(fecha);
  if (!isValid(date)) return undefined;
  const local = fecha.length === 10 ? date : toZonedTime(date, TIMEZONE);
  const dias = differenceInCalendarDays(toZonedTime(ahora, TIMEZONE), local);
  return dias < 0 ? undefined : dias;
}

export function calcularPrioridad(conteo: ConteoInicio, ahora: Date): PrioridadInicio {
  const { total, atrasadas = 0 } = conteo;
  const dias = total > 0 ? antiguedadDias(conteo.fechaMasAntigua, ahora) : undefined;
  const u = UMBRALES_INICIO;
  const nivel: NivelInicio =
    total <= 0
      ? 'normal'
      : (dias ?? 0) > u.diasCritico || total > u.cantidadCritico || atrasadas > u.atrasadasCritico
        ? 'critico'
        : (dias ?? 0) > u.diasAtencion || total > u.cantidadAtencion || atrasadas > 0
          ? 'atencion'
          : 'normal';
  const antiguedad =
    dias === undefined ? '' : dias === 0 ? 'hoy' : dias === 1 ? 'ayer' : `hace ${dias} días`;
  const base = total === 1 ? '1 pendiente' : `${total} pendientes`;
  const motivo =
    total === 0
      ? 'Sin pendientes'
      : `${base}${antiguedad ? ` · el más antiguo: ${antiguedad}` : ''}${atrasadas > 0 ? ` · ${atrasadas} atrasadas` : ''}`;
  return { nivel, dias, total, motivo, antiguedad, urgente: (dias ?? 0) > u.diasAtencion };
}

const orden: Record<NivelInicio, number> = { critico: 2, atencion: 1, normal: 0 };
/** Desempate: antigüedad, cantidad y finalmente orden estable del catálogo. */
export function compararPrioridad(a: PrioridadInicio, b: PrioridadInicio): number {
  return orden[b.nivel] - orden[a.nivel] || (b.dias ?? -1) - (a.dias ?? -1) || b.total - a.total;
}

export const estiloNivel = {
  normal: { texto: 'text-ink-muted', punto: 'bg-info-solid', etiqueta: null, variante: 'neutral' },
  atencion: {
    texto: 'text-warning-fg',
    punto: 'bg-warning-solid',
    etiqueta: 'Atención',
    variante: 'warning',
  },
  critico: {
    texto: 'text-danger-fg',
    punto: 'bg-danger-solid',
    etiqueta: 'Crítico',
    variante: 'danger',
  },
} as const;
