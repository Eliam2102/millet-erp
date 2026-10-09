import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { CancelarOcConRecepcionesValues } from '@/features/compras/ordenes/schemas/cancelar-doble-firma';

export interface CancelarOrdenCompraConRecepcionesMutationArgs {
  ordenCompraId: string;
  command: CancelarOcConRecepcionesValues;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/** Registra la solicitud de cancelación (primera firma). */
export function useCancelarOrdenCompraConRecepciones() {
  const queryClient = useQueryClient();

  return useMutation<
    void,
    Error,
    CancelarOrdenCompraConRecepcionesMutationArgs
  >({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/cancelar-con-recepciones`,
        {
          method: 'POST',
          body: command,
          idempotencyKey,
        },
      );
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(ordenCompraId),
      });
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.all,
      });
    },
  });
}
