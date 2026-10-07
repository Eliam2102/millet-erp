import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * Periodos contables (F1-CON-03, docs/modulos/contabilidad/09-plan-periodos-contables.md §6).
 * Mirror de `PeriodosCommands.cs`: fechas `yyyy-MM-dd` (DateOnly), instantes ISO y enums como texto.
 */
export type EstadoPeriodo = 'NoAbierto' | 'Abierto' | 'Cerrado';
export type AccionPeriodo = 'Abrir' | 'Cerrar' | 'Reabrir';

export interface PeriodoContable {
  id: string;
  ejercicioId: string;
  anio: number;
  numero: number;
  nombre: string;
  fechaInicio: string;
  fechaFin: string;
  estado: EstadoPeriodo;
  esAjuste: boolean;
  abiertoPor: string | null;
  abiertoEn: string | null;
  cerradoPor: string | null;
  cerradoEn: string | null;
  reabiertoPor: string | null;
  reabiertoEn: string | null;
  version: number;
}

/** `version` es la del ejercicio: el If-Match de la apertura en lote. */
export interface EjercicioContable {
  id: string;
  anio: number;
  version: number;
  periodos: PeriodoContable[];
}

export interface BitacoraPeriodo {
  id: string;
  accion: AccionPeriodo;
  estadoAnterior: EstadoPeriodo;
  estadoNuevo: EstadoPeriodo;
  motivo: string | null;
  usuarioId: string | null;
  usuarioNombre: string;
  ocurridoEn: string;
  versionResultante: number;
}

/** D7: motivo de cerrar y reabrir (mismo rango que el backend). */
export const MOTIVO_MINIMO = 10;
export const MOTIVO_MAXIMO = 500;

const BASE = '/api/v1/contabilidad/periodos';

export const periodoKeys = {
  all: ['contabilidad', 'periodos'] as const,
  ejercicios: ['contabilidad', 'periodos', 'ejercicios'] as const,
  bitacora: (periodoId: string) => ['contabilidad', 'periodos', 'bitacora', periodoId] as const,
};

function useInvalidar() {
  const qc = useQueryClient();
  return () => void qc.invalidateQueries({ queryKey: periodoKeys.all });
}

export function useEjercicios() {
  return useQuery({
    queryKey: periodoKeys.ejercicios,
    queryFn: async ({ signal }) => (await apiRequest<EjercicioContable[]>(`${BASE}/ejercicios`, { signal })).data,
  });
}

export function useBitacoraPeriodo(periodoId: string | null) {
  return useQuery({
    queryKey: periodoKeys.bitacora(periodoId ?? ''),
    enabled: !!periodoId,
    queryFn: async ({ signal }) => (await apiRequest<BitacoraPeriodo[]>(`${BASE}/${periodoId}/bitacora`, { signal })).data,
  });
}

export function useCrearEjercicio() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { anio: number; idempotencyKey: string }) =>
      (await apiRequest<EjercicioContable>(`${BASE}/ejercicios`, { method: 'POST', body: { anio: a.anio }, idempotencyKey: a.idempotencyKey })).data,
    onSuccess: invalidar,
  });
}

/** Abre en lote (todo o nada) con la versión del ejercicio. */
export function useAbrirPeriodos() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { ejercicio: EjercicioContable; numeros: number[]; motivo: string; idempotencyKey: string }) =>
      (await apiRequest<EjercicioContable>(`${BASE}/ejercicios/${a.ejercicio.id}/abrir`, {
        method: 'POST',
        body: { numeros: a.numeros, motivo: a.motivo.trim() || null },
        idempotencyKey: a.idempotencyKey,
        ifMatch: String(a.ejercicio.version),
      })).data,
    onSuccess: invalidar,
  });
}

/** Cierra o reabre un periodo con motivo obligatorio y la versión del periodo. */
export function useTransicionPeriodo() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { periodo: PeriodoContable; accion: 'cerrar' | 'reabrir'; motivo: string; idempotencyKey: string }) =>
      (await apiRequest<PeriodoContable>(`${BASE}/${a.periodo.id}/${a.accion}`, {
        method: 'POST',
        body: { motivo: a.motivo.trim() },
        idempotencyKey: a.idempotencyKey,
        ifMatch: String(a.periodo.version),
      })).data,
    onSuccess: invalidar,
  });
}
