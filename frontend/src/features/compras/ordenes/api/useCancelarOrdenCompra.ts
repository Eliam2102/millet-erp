import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { CancelarOcValues } from '@/features/compras/ordenes/schemas/cancelar';

export interface CancelarOrdenCompraMutationArgs {
  ordenCompraId: string;
  command: CancelarOcValues;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useCancelarOrdenCompra()</c> — POST <c>/{id}/cancelar</c>
 * (UF5-PR1, F3-PR3). Cancela una OC sin recepciones. Permiso requerido:
 * <c>compras.ordenes.cancelar</c>. Visible en <c>Borrador</c>,
 * <c>EnAutorizacion*</c>, <c>Rechazada</c>, y <c>Autorizada con
 * subEstadoRecepcion=SinRecepcion</c> (gateado por la matriz §6.1).
 *
 * <para>Para cancelar una OC con recepciones parciales/completas, usar
 * <c>useCancelarOrdenCompraConRecepciones</c> (doble firma).</para>
 */
export function useCancelarOrdenCompra() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, CancelarOrdenCompraMutationArgs>({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/cancelar`,
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
