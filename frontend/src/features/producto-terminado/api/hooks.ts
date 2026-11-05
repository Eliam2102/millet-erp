import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import type { EscaneoDto, EscaneosPagina, PiezasPagina, ResultadoEscaneo, SalidasPendientesPagina, TraspasoDto, TraspasosPagina } from './types';

const BASE = '/api/v1/producto-terminado';

export const ptKeys = {
  all: ['producto-terminado'] as const,
};

function qs(params: Record<string, string | number | undefined | null>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : '';
}

export function usePiezas(f: { pedido?: string; sucursalId?: string; page?: number; pageSize?: number }) {
  return useQuery({
    queryKey: [...ptKeys.all, 'piezas', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<PiezasPagina>(`${BASE}/piezas${qs(f)}`, { signal })).data,
  });
}

export function useEscaneos(f: { resultado?: ResultadoEscaneo; page?: number; pageSize?: number }) {
  return useQuery({
    queryKey: [...ptKeys.all, 'escaneos', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<EscaneosPagina>(`${BASE}/escaneos${qs(f)}`, { signal })).data,
  });
}

export type EscaneoAccionRuta = 'reintentar' | 'descartar' | 'revisar';

export function useEscaneoAccion() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const conflict = useConflictDialog();
  return useMutation({
    mutationFn: ({ escaneo, ruta, body }: { escaneo: Pick<EscaneoDto, 'id' | 'version'>; ruta: EscaneoAccionRuta; body?: { motivo: string } }) =>
      apiRequest<EscaneoDto>(`${BASE}/escaneos/${escaneo.id}/${ruta}`, {
        method: 'POST',
        body,
        // apiRequest ya envuelve el valor entre comillas (If-Match: "<version>").
        ifMatch: String(escaneo.version),
        idempotencyKey: keyFor({ id: escaneo.id, version: escaneo.version, ruta, body }),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
    onError: (error) => {
      if (esConflictoConcurrencia(error))
        conflict.openSimple({ onRefrescar: () => qc.invalidateQueries({ queryKey: ptKeys.all }) });
    },
  });
}

/** Pedidos en 70 que aún no salen; se refresca solo cada minuto. */
export function useSalidasPendientes(f: {
  sucursalId?: string;
  alerta?: '24h' | '48h';
  buscar?: string;
  page?: number;
  pageSize?: number;
}) {
  return useQuery({
    queryKey: [...ptKeys.all, 'salidas-pendientes', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<SalidasPendientesPagina>(`${BASE}/salidas-pendientes${qs(f)}`, { signal })).data,
    refetchInterval: 60_000,
  });
}

export function useTraspasos(f: { estado?: string; page?: number; pageSize?: number }) {
  return useQuery({
    queryKey: [...ptKeys.all, 'traspasos', f],
    queryFn: async ({ signal }) =>
      (await apiRequest<TraspasosPagina>(`${BASE}/traspasos${qs(f)}`, { signal })).data,
  });
}

export function useTraspaso(id: string | null) {
  return useQuery({
    queryKey: [...ptKeys.all, 'traspaso', id],
    enabled: id !== null,
    queryFn: async ({ signal }) => (await apiRequest<TraspasoDto>(`${BASE}/traspasos/${id}`, { signal })).data,
  });
}

export function useCrearTraspaso() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: (body: { sucursalOrigenId: string; sucursalDestinoId: string; etiquetas: string[] }) =>
      apiRequest<TraspasoDto>(`${BASE}/traspasos`, { method: 'POST', body, idempotencyKey: keyFor(body) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}

/** `piezaIds` ausente = recepción total; una lista parcial deja la diferencia pendiente. */
export function useRecibirTraspaso() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: ({ id, piezaIds }: { id: string; piezaIds?: string[] }) =>
      apiRequest<TraspasoDto>(`${BASE}/traspasos/${id}/recibir`, {
        method: 'POST',
        body: piezaIds ? { piezaIds } : undefined,
        idempotencyKey: keyFor({ id, piezaIds }),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}
