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

/**
 * <c>useCancelarOrdenCompraConRecepciones()</c> — POST
 * <c>/{id}/cancelar-con-recepciones</c> (UF5-PR1, F5-PR4). Cancela una
 * OC con recepciones parciales o completas. Requiere los <b>3 permisos</b>
 * simultáneos en el usuario actual:
 * <c>compras.ordenes.cancelar-doble</c> +
 * <c>compras.ordenes.autorizar-nivel1</c> +
 * <c>compras.ordenes.autorizar-nivel2</c>.
 *
 * <para>Para cada línea con RQ asociada y saldo no recibido, libera la
 * cantidad no recibida al pool de la RQ origen vía
 * <c>LineaRqLiberadaEvent</c>.</para>
 */
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
