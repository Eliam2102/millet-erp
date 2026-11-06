import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from './keys';
import type { RespuestaProveedorOc } from './types';

/** G-11: marca como errónea una respuesta del proveedor sin borrarla del historial. */
export function useAnularRespuestaProveedor(ordenCompraId: string) {
  const queryClient = useQueryClient();
  return useMutation<RespuestaProveedorOc, Error, { respuestaId: string; motivo: string }>({
    mutationFn: async ({ respuestaId, motivo }) =>
      (
        await apiRequest<RespuestaProveedorOc>(
          `/api/v1/compras/ordenes/${ordenCompraId}/respuestas-proveedor/${respuestaId}/anular`,
          { method: 'POST', body: { motivo }, idempotencyKey: crypto.randomUUID() },
        )
      ).data,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ordenesKeys.detail(ordenCompraId) });
      queryClient.invalidateQueries({ queryKey: ordenesKeys.all });
    },
  });
}
