import { z } from 'zod';
import { esApiError } from '@/lib/api';
import type { CausaRechazo, EstadoRechazo } from '../api/motor-types';
import { mensajePoliza } from './polizas';

type VarianteBadge = 'success' | 'warning' | 'info' | 'neutral' | 'danger';

/** Causa del rechazo en lenguaje de usuario (C1.4). */
export const TEXTO_CAUSA: Record<CausaRechazo, string> = {
  SinPlantilla: 'Sin plantilla',
  RolSinCuenta: 'Rol sin cuenta',
  PeriodoCerrado: 'Periodo cerrado',
  CuentaNoValida: 'Cuenta no válida',
  DatoFaltante: 'Dato faltante',
  NoCuadra: 'No cuadra',
};

/** Todas las causas son bloqueos que Contabilidad resuelve: warning las de configuración, danger las de datos. */
export const VARIANTE_CAUSA: Record<CausaRechazo, VarianteBadge> = {
  SinPlantilla: 'warning',
  RolSinCuenta: 'warning',
  PeriodoCerrado: 'warning',
  CuentaNoValida: 'danger',
  DatoFaltante: 'danger',
  NoCuadra: 'danger',
};

/** Un estado = un color (DESIGN §2.1). */
export const ESTADO_RECHAZO_UI: Record<EstadoRechazo, { texto: string; variante: VarianteBadge }> = {
  Pendiente: { texto: 'Pendiente', variante: 'warning' },
  Reprocesado: { texto: 'Reprocesado', variante: 'success' },
  Descartado: { texto: 'Descartado', variante: 'neutral' },
};

export function textoCausa(causa: string | null | undefined): string {
  if (!causa) return '';
  return TEXTO_CAUSA[causa as CausaRechazo] ?? causa;
}

/** Reglas del backend (C1.4). */
export const MOTIVO_DESCARTE_MINIMO = 10;
export const MOTIVO_DESCARTE_MAXIMO = 1000;
export const CODIGO_ROL_MAXIMO = 60;
export const CODIGO_ROL_PATRON = /^[A-Z0-9_]+$/;

/** Mensaje del servidor en lenguaje de usuario; el conflicto de versión habla del rol. */
export function mensajeMotor(e: unknown): string {
  if (esApiError(e) && e.status === 409 && e.code === 'CONCURRENCY_CONFLICT') {
    return 'Alguien más cambió este rol. Recarga para ver la cuenta actual.';
  }
  if (esApiError(e) && e.status === 428) return 'Falta la versión del rol. Recarga e intenta de nuevo.';
  return mensajePoliza(e);
}

/** El payload llega como texto JSON; se muestra indentado si se puede leer. */
export function payloadLegible(payload: string): string {
  try {
    return JSON.stringify(JSON.parse(payload), null, 2);
  } catch {
    return payload;
  }
}

// ─── Search params ────────────────────────────────────────────────────────────

export const VISTAS_MOTOR = ['roles', 'plantillas', 'rechazos'] as const;
export type VistaMotor = (typeof VISTAS_MOTOR)[number];

export const MotorSearchSchema = z.object({
  vista: z.enum(VISTAS_MOTOR).optional().catch(undefined),
  /** Filtro de rechazos; sin valor = Pendiente. `Todos` quita el filtro. */
  estado: z.enum(['Pendiente', 'Reprocesado', 'Descartado', 'Todos']).optional().catch(undefined),
});

export type MotorSearch = z.infer<typeof MotorSearchSchema>;
