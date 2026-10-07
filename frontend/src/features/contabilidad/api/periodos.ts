import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

const BASE = '/api/v1/contabilidad/periodos';

export type EstadoPeriodo = 'Abierto' | 'Cerrado';
export type AccionPeriodo = 'Creado' | 'Cerrado' | 'Reabierto';

export interface Periodo {
  id: string;
  ejercicio: number;
  numero: number;
  etiqueta: string;
  esPeriodoAjustes: boolean;
  estado: EstadoPeriodo;
  fechaInicio: string | null;
  fechaFin: string | null;
  cerradoPor: string | null;
  cerradoEn: string | null;
  reabiertoPor: string | null;
  reabiertoEn: string | null;
  version: number;
}

export interface EventoPeriodo {
  id: string;
  accion: AccionPeriodo;
  usuario: string;
  fecha: string;
  motivo: string | null;
}

export const periodoKeys = {
  all: ['contabilidad', 'periodos'] as const,
  ejercicios: ['contabilidad', 'periodos', 'ejercicios'] as const,
  lista: (ejercicio: number) => ['contabilidad', 'periodos', 'lista', ejercicio] as const,
  historial: (id: string) => ['contabilidad', 'periodos', 'historial', id] as const,
};

export function useEjercicios() {
  return useQuery({
    queryKey: periodoKeys.ejercicios,
    queryFn: async ({ signal }) => (await apiRequest<number[]>(`${BASE}/ejercicios`, { signal })).data,
  });
}

export function usePeriodos(ejercicio: number | null) {
  return useQuery({
    queryKey: periodoKeys.lista(ejercicio ?? 0),
    queryFn: async ({ signal }) => (await apiRequest<Periodo[]>(`${BASE}?ejercicio=${ejercicio}`, { signal })).data,
    enabled: ejercicio !== null,
  });
}

export function useHistorialPeriodo(id: string | null) {
  return useQuery({
    queryKey: periodoKeys.historial(id ?? ''),
    queryFn: async ({ signal }) => (await apiRequest<EventoPeriodo[]>(`${BASE}/${id}/historial`, { signal })).data,
    enabled: id !== null,
  });
}

function useInvalidar() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: periodoKeys.all });
}

export function useCrearEjercicio() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (ejercicio: number) =>
      (await apiRequest<{ ejercicio: number; periodos: Periodo[] }>(`${BASE}/ejercicios`, {
        method: 'POST',
        body: { ejercicio },
        idempotencyKey: crypto.randomUUID(),
      })).data,
    onSuccess: invalidar,
  });
}

/** Cerrar (motivo opcional) o reabrir (motivo obligatorio, solo Contador General). Exige la versión vista (If-Match). */
export function useAccionPeriodo() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async ({ periodo, accion, motivo }: { periodo: Periodo; accion: 'cerrar' | 'reabrir'; motivo: string | null }) =>
      (await apiRequest<Periodo>(`${BASE}/${periodo.id}/${accion}`, {
        method: 'POST',
        body: { motivo },
        idempotencyKey: crypto.randomUUID(),
        ifMatch: String(periodo.version),
      })).data,
    onSuccess: invalidar,
  });
}
