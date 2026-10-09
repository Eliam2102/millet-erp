import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { RetencionForm, ReglaRetencion } from '@/features/cxp/lib/retenciones-p8';

export interface RetencionCatalogo extends ReglaRetencion {
  id: string;
  descripcion: string;
  fuente: string;
  aviso: string;
  motivoCambio: string;
  version: number;
}
const key = ['cxp', 'retenciones'] as const;
const path = '/api/v1/cuentas-por-pagar/catalogos/retenciones';
export function useRetenciones(enabled = true) {
  return useQuery({
    queryKey: key,
    queryFn: async ({ signal }) => (await apiRequest<RetencionCatalogo[]>(path, { signal })).data,
    enabled,
  });
}
export function useGuardarRetencion() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (form: RetencionForm) =>
      (
        await apiRequest<RetencionCatalogo>(form.id ? `${path}/${form.id}` : path, {
          method: form.id ? 'PUT' : 'POST',
          body: form,
          idempotencyKey: crypto.randomUUID(),
          headers: form.id ? { 'X-Expected-Version': String(form.version) } : undefined,
        })
      ).data,
    onSuccess: () => client.invalidateQueries({ queryKey: key }),
  });
}
