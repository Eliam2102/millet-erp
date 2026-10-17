import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { RespuestaProveedorOc } from '@/features/compras/ordenes/api/types';
import type { RegistrarRespuestaProveedorValues } from '@/features/compras/ordenes/schemas/respuesta-proveedor';

export interface RegistrarRespuestaProveedorMutationArgs {
  ordenCompraId: string;
  command: RegistrarRespuestaProveedorValues;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/** F1-COM-11: registra la confirmación o el rechazo del proveedor sobre una OC autorizada. */
export function useRegistrarRespuestaProveedor() {
  const queryClient = useQueryClient();

  return useMutation<RespuestaProveedorOc, Error, RegistrarRespuestaProveedorMutationArgs>({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) =>
      (
        await apiRequest<RespuestaProveedorOc>(
          `/api/v1/compras/ordenes/${ordenCompraId}/respuestas-proveedor`,
          { method: 'POST', body: command, idempotencyKey },
        )
      ).data,
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({ queryKey: ordenesKeys.detail(ordenCompraId) });
      queryClient.invalidateQueries({ queryKey: ordenesKeys.all });
    },
  });
}
