import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

export interface RemoverAdjuntoMutationArgs {
  ordenCompraId: string;
  adjuntoId: string;
}

/**
 * <c>useRemoverAdjunto()</c> — DELETE <c>/{id}/adjuntos/{adjuntoId}</c>.
 * Solo permitido en <c>Borrador</c> (matriz §6.1
 * <c>accionRemoverAdjunto</c>; backend valida de nuevo). NO requiere
 * Idempotency-Key — DELETE es naturalmente idempotente.
 *
 * <para>onSuccess invalida el detalle para que el AdjuntosManager
 * refleje la lista actualizada.</para>
 */
export function useRemoverAdjunto() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, RemoverAdjuntoMutationArgs>({
    mutationFn: async ({ ordenCompraId, adjuntoId }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/adjuntos/${adjuntoId}`,
        { method: 'DELETE' },
      );
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(ordenCompraId),
      });
    },
  });
}
