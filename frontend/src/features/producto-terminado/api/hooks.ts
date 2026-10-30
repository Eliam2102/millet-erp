import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import type { EscaneoDto, EscaneosPagina, PiezasPagina, ResultadoEscaneo } from './types';

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
