import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import type { CrearExpediente, ExpedienteDetalle, Filtros, OcImportacion, Pagina } from './types';
export const CE_BASE = '/api/v1/comercio-exterior/expedientes';
export const ceKeys = {
  all: ['comercio-exterior'] as const,
  detalle: (id: string) => ['comercio-exterior', 'detalle', id] as const,
};
export function useExpedientes(filtros: Filtros) {
  const params = new URLSearchParams();
  Object.entries(filtros).forEach(([k, v]) => {
    if (v !== undefined && v !== '') params.set(k, String(v));
  });
  params.set('limit', '10');
  return useQuery({
    queryKey: [...ceKeys.all, 'lista', filtros],
    queryFn: async ({ signal }) =>
      (await apiRequest<Pagina>(`${CE_BASE}?${params}`, { signal })).data,
  });
}
export function useExpediente(id: string) {
  return useQuery({
    queryKey: ceKeys.detalle(id),
    queryFn: ({ signal }) => apiRequest<ExpedienteDetalle>(`${CE_BASE}/${id}`, { signal }),
  });
}
export function useOrdenesImportacion(enabled: boolean, q = '') {
  return useQuery({
    queryKey: [...ceKeys.all, 'ordenes', q],
    enabled,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<OcImportacion[]>(
          `${CE_BASE}/ordenes-importacion?q=${encodeURIComponent(q)}`,
          { signal },
        )
      ).data,
  });
}
export function useCrearExpediente() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: (body: CrearExpediente) =>
      apiRequest<ExpedienteDetalle>(CE_BASE, {
        method: 'POST',
        body,
        idempotencyKey: keyFor(body),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ceKeys.all }),
  });
}
export function useAccionExpediente(id: string) {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const conflict = useConflictDialog();
  return useMutation({
    mutationFn: async ({
      accion,
      body,
      method = 'POST',
    }: {
      accion: string;
      body?: unknown;
      method?: string;
    }) => {
      const cache = qc.getQueryData<{ etag?: string }>(ceKeys.detalle(id));
      return apiRequest<ExpedienteDetalle>(`${CE_BASE}/${id}${accion ? `/${accion}` : ''}`, {
        method,
        body,
        ifMatch: cache?.etag,
        idempotencyKey: keyFor({ accion, body, etag: cache?.etag }),
      });
    },
    onSuccess: async (result) => {
      qc.setQueryData(ceKeys.detalle(id), result);
      await qc.invalidateQueries({ queryKey: ceKeys.all });
    },
    onError: (error) => {
      if (esConflictoConcurrencia(error))
        conflict.openSimple({ onRefrescar: () => qc.invalidateQueries({ queryKey: ceKeys.all }) });
    },
  });
}
