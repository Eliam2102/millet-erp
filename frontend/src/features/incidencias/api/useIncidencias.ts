import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { incidenciasKeys, type FiltrosDocumento, type FiltrosMias } from './keys';
import type {
  AbrirIncidenciaRequest,
  IncidenciaDetalle,
  PaginaIncidencias,
  PasoIncidencia,
  ResumenMisIncidencias,
} from './types';

const BASE = '/api/v1/incidencias';

function query(params: Record<string, string | number | string[] | undefined | null>): string {
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v == null || v === '') continue;
    if (Array.isArray(v)) v.forEach((x) => qs.append(k, x));
    else qs.append(k, String(v));
  }
  const s = qs.toString();
  return s ? `?${s}` : '';
}

/** «Mis incidencias»: las asignadas al usuario actual. Sin `estado`, el servidor devuelve las pendientes. */
export function useMisIncidencias(filtros: FiltrosMias) {
  return useQuery({
    queryKey: incidenciasKeys.mias(filtros),
    queryFn: async ({ signal }) =>
      (await apiRequest<PaginaIncidencias>(`${BASE}/mias${query({ ...filtros })}`, { signal })).data,
  });
}

/** Conteo de la campana: abiertas y en atención asignadas al usuario. */
export function useResumenMisIncidencias(habilitado: boolean) {
  return useQuery({
    queryKey: incidenciasKeys.resumen(),
    enabled: habilitado,
    refetchInterval: 60_000,
    queryFn: async ({ signal }) =>
      (await apiRequest<ResumenMisIncidencias>(`${BASE}/mias/resumen`, { signal })).data,
  });
}

/** Incidencias de un documento (todas las del alcance del usuario, de la más nueva a la más vieja). */
export function useIncidenciasDocumento(filtros: FiltrosDocumento, habilitado = true) {
  return useQuery({
    queryKey: incidenciasKeys.documento(filtros),
    enabled: habilitado && Boolean(filtros.documentoId),
    queryFn: async ({ signal }) =>
      (await apiRequest<PaginaIncidencias>(`${BASE}${query({ ...filtros, limit: 50 })}`, { signal })).data,
  });
}

interface DetalleCache {
  data: IncidenciaDetalle;
  etag?: string;
}

export function useIncidencia(id: string) {
  return useQuery({
    queryKey: incidenciasKeys.detalle(id),
    enabled: Boolean(id),
    queryFn: async ({ signal }): Promise<DetalleCache> => {
      const r = await apiRequest<IncidenciaDetalle>(`${BASE}/${id}`, { signal });
      return { data: r.data, etag: r.etag };
    },
    select: (c) => c.data,
  });
}

export function usePasosIncidencia() {
  return useQuery({
    queryKey: incidenciasKeys.pasos(),
    staleTime: 10 * 60_000,
    queryFn: async ({ signal }) => (await apiRequest<PasoIncidencia[]>(`${BASE}/pasos`, { signal })).data,
  });
}

export function useAbrirIncidencia() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ body, idempotencyKey }: { body: AbrirIncidenciaRequest; idempotencyKey: string }) => {
      const r = await apiRequest<IncidenciaDetalle>(BASE, { method: 'POST', body, idempotencyKey });
      return { detalle: r.data, etag: r.etag, yaExistia: r.status === 200 };
    },
    onSuccess: ({ detalle, etag }) => {
      qc.setQueryData<DetalleCache>(incidenciasKeys.detalle(detalle.id), { data: detalle, etag });
      void qc.invalidateQueries({ queryKey: incidenciasKeys.all });
    },
  });
}

export type AccionIncidencia = 'atender' | 'resolver' | 'cancelar' | 'reasignar';

export interface AccionIncidenciaArgs {
  accion: AccionIncidencia;
  body: Record<string, unknown>;
  idempotencyKey: string;
}

/** Transiciones de una incidencia con If-Match del detalle en caché (ADR-0012). */
export function useAccionIncidencia(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ accion, body, idempotencyKey }: AccionIncidenciaArgs) => {
      const etag = qc.getQueryData<DetalleCache>(incidenciasKeys.detalle(id))?.etag;
      const r = await apiRequest<IncidenciaDetalle>(`${BASE}/${id}/${accion}`, {
        method: 'POST',
        body,
        idempotencyKey,
        ifMatch: etag,
      });
      return { data: r.data, etag: r.etag };
    },
    onSuccess: (c) => {
      qc.setQueryData<DetalleCache>(incidenciasKeys.detalle(id), c);
      void qc.invalidateQueries({ queryKey: incidenciasKeys.all, predicate: (q) => q.queryKey[1] !== 'detalle' });
    },
  });
}
