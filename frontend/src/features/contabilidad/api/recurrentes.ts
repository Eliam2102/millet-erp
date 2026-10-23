import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { contabKeys } from './hooks';
import { polizaKeys } from './polizas';
import type { Cuenta } from './types';
import type {
  AccionPlantilla,
  EstadoPlantillaRecurrente,
  PlantillaRecurrente,
  PlantillaRecurrenteRequest,
  PlantillaRecurrenteResumen,
  ResultadoGeneracion,
} from './recurrentes-types';

/** Pólizas recurrentes (K10.4). Mutaciones con Idempotency-Key y, sobre una plantilla existente, If-Match con su versión. */
const BASE = '/api/v1/contabilidad/polizas-recurrentes';

export const recurrenteKeys = {
  all: ['contabilidad', 'recurrentes'] as const,
  lista: (estado: EstadoPlantillaRecurrente | undefined) => ['contabilidad', 'recurrentes', 'lista', estado ?? 'todas'] as const,
  detalle: (id: string) => ['contabilidad', 'recurrentes', 'detalle', id] as const,
};

/** Generar y activar crean pólizas: se invalidan también la bandeja y los detalles de pólizas. */
function useInvalidar() {
  const qc = useQueryClient();
  return () => {
    void qc.invalidateQueries({ queryKey: recurrenteKeys.all });
    void qc.invalidateQueries({ queryKey: polizaKeys.all });
  };
}

export function usePlantillasRecurrentes(estado?: EstadoPlantillaRecurrente) {
  return useQuery({
    queryKey: recurrenteKeys.lista(estado),
    queryFn: async ({ signal }) =>
      (await apiRequest<PlantillaRecurrenteResumen[]>(`${BASE}${estado ? `?estado=${estado}` : ''}`, { signal })).data,
  });
}

export function usePlantillaRecurrente(id: string | null) {
  return useQuery({
    queryKey: recurrenteKeys.detalle(id ?? 'none'),
    enabled: !!id,
    queryFn: async ({ signal }) => (await apiRequest<PlantillaRecurrente>(`${BASE}/${id}`, { signal })).data,
  });
}

export function useCrearPlantilla() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { body: PlantillaRecurrenteRequest; idempotencyKey: string }) =>
      (await apiRequest<PlantillaRecurrente>(BASE, { method: 'POST', body: a.body, idempotencyKey: a.idempotencyKey })).data,
    onSuccess: invalidar,
  });
}

export function useEditarPlantilla() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { id: string; version: number; body: PlantillaRecurrenteRequest; idempotencyKey: string }) =>
      (await apiRequest<PlantillaRecurrente>(`${BASE}/${a.id}`, {
        method: 'PUT', body: a.body, idempotencyKey: a.idempotencyKey, ifMatch: String(a.version),
      })).data,
    onSuccess: invalidar,
  });
}

/** Activar (otra persona, no quien la preparó), suspender o reanudar. Activar y reanudar generan lo que ya toca. */
export function useCambiarEstadoPlantilla() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { plantilla: Pick<PlantillaRecurrente, 'id' | 'version'>; accion: AccionPlantilla; idempotencyKey: string }) =>
      (await apiRequest<PlantillaRecurrente>(`${BASE}/${a.plantilla.id}/${a.accion}`, {
        method: 'POST', idempotencyKey: a.idempotencyKey, ifMatch: String(a.plantilla.version),
      })).data,
    onSuccess: invalidar,
  });
}

/** Corre ahora lo que el proceso de fondo genera cada hora (idempotente); sin plantilla, todas las activas. */
export function useGenerarPendientes() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { plantillaId?: string; idempotencyKey: string }) =>
      (await apiRequest<ResultadoGeneracion>(`${BASE}/generar${a.plantillaId ? `?plantillaId=${a.plantillaId}` : ''}`, {
        method: 'POST', idempotencyKey: a.idempotencyKey,
      })).data,
    onSuccess: invalidar,
  });
}

/**
 * Código y nombre de las cuentas de una plantilla: el DTO de la plantilla solo trae `cuentaId`. Usa el GET por id del
 * catálogo (cacheado con la misma llave que el detalle de cuenta); sin permiso de catálogo se queda sin etiqueta.
 */
export function useEtiquetasCuentas(ids: readonly string[]): ReadonlyMap<string, { codigo: string; nombre: string }> {
  const unicos = [...new Set(ids.filter(Boolean))];
  return useQueries({
    queries: unicos.map((id) => ({
      queryKey: [...contabKeys.detalle(id), 'etiqueta'] as const,
      retry: 0,
      staleTime: 5 * 60_000,
      queryFn: async ({ signal }: { signal: AbortSignal }) => (await apiRequest<Cuenta>(`/api/v1/contabilidad/cuentas/${id}`, { signal })).data,
    })),
    combine: combinarEtiquetas,
  });
}

/** Fuera del hook: con una referencia estable, `combine` devuelve el mismo mapa mientras los datos no cambien. */
function combinarEtiquetas(rs: { data?: Cuenta }[]): ReadonlyMap<string, { codigo: string; nombre: string }> {
  const mapa = new Map<string, { codigo: string; nombre: string }>();
  for (const r of rs) if (r.data) mapa.set(r.data.id, { codigo: r.data.codigo, nombre: r.data.nombre });
  return mapa;
}
