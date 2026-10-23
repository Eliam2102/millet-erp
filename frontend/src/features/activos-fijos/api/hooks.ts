import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import type { Activo, Clase } from './types';
export const AF_BASE = '/api/v1/activos-fijos';
export const afKeys = {
  all: ['activos-fijos'] as const,
  detalle: (id: string) => ['activos-fijos', 'detalle', id] as const,
};
export function useClasesActivo(enabled = true) {
  return useQuery({
    queryKey: [...afKeys.all, 'clases'],
    enabled,
    queryFn: async ({ signal }) =>
      (await apiRequest<Clase[]>(`${AF_BASE}/clases`, { signal })).data,
  });
}
export function useActivos(filtros: Record<string, string | number | undefined>) {
  const params = new URLSearchParams();
  Object.entries(filtros).forEach(([k, v]) => {
    if (v !== undefined && v !== '') params.set(k, String(v));
  });
  return useQuery({
    queryKey: [...afKeys.all, 'lista', filtros],
    queryFn: async ({ signal }) =>
      (
        await apiRequest<{ items: Activo[]; total: number }>(`${AF_BASE}/activos?${params}`, {
          signal,
        })
      ).data,
  });
}
export function useActivo(id: string) {
  return useQuery({
    queryKey: afKeys.detalle(id),
    queryFn: ({ signal }) => apiRequest<Activo>(`${AF_BASE}/activos/${id}`, { signal }),
  });
}
export function useGuardarActivo() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const conflicto = useConflictDialog();
  return useMutation({
    mutationFn: ({
      ruta,
      body,
      version,
      method = 'POST',
    }: {
      ruta: string;
      body: unknown;
      version?: number;
      method?: string;
    }) =>
      apiRequest<Activo>(`${AF_BASE}/${ruta}`, {
        method,
        body,
        idempotencyKey: keyFor({ ruta, body, version }),
        ifMatch: version ? `"${version}"` : undefined,
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: afKeys.all }),
    onError: (error) => {
      if (esConflictoConcurrencia(error))
        conflicto.openSimple({ onRefrescar: () => qc.invalidateQueries({ queryKey: afKeys.all }) });
    },
  });
}
