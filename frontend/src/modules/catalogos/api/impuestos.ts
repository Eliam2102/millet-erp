import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type {
  ActualizarImpuestoReferenciaPayload,
  CrearImpuestoReferenciaPayload,
  ImpuestoReferenciaResponse,
} from '@/modules/catalogos/api/types';

export function useImpuestosReferencia(fecha: string, incluirHistorico = false, enabled = true) {
  return useQuery({
    enabled,
    queryKey: [...catalogosKeys.impuestos(), fecha, incluirHistorico],
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ fecha, incluirHistorico: String(incluirHistorico) });
      const { data } = await apiRequest<ImpuestoReferenciaResponse[]>(
        `/api/v1/catalogos/impuestos?${params}`, { signal });
      return data;
    },
  });
}

export function useCrearImpuestoReferencia() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (payload: CrearImpuestoReferenciaPayload) => {
      const { data } = await apiRequest<ImpuestoReferenciaResponse>(
        '/api/v1/catalogos/impuestos',
        { method: 'POST', body: payload, idempotencyKey: crypto.randomUUID() });
      return data;
    },
    onSuccess: () => client.invalidateQueries({ queryKey: catalogosKeys.impuestos() }),
  });
}

export function useActualizarImpuestoReferencia() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, payload }: {
      id: string;
      payload: ActualizarImpuestoReferenciaPayload;
    }) => {
      const { data } = await apiRequest<ImpuestoReferenciaResponse>(
        `/api/v1/catalogos/impuestos/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey: crypto.randomUUID() });
      return data;
    },
    onSuccess: () => client.invalidateQueries({ queryKey: catalogosKeys.impuestos() }),
  });
}
