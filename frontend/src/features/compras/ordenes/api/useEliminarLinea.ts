import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

export interface EliminarLineaMutationArgs {
  ordenCompraId: string;
  lineaId: string;
}

/**
 * <c>useEliminarLinea()</c> — DELETE
 * <c>/api/v1/compras/ordenes/{id}/lineas/{lineaId}</c>. Solo en
 * <c>Borrador</c>/<c>Rechazada</c> y si la línea no tiene recepción
 * ni facturación (validación backend).
 *
 * <para>El backend explicítamente NO requiere
 * <c>Idempotency-Key</c> — eliminar es naturalmente idempotente
 * (segunda llamada con la misma URL = 404 RES_NO_ENCONTRADA, no
 * efecto adicional).</para>
 *
 * <para>onSuccess invalida el detalle de la OC para que el editor
 * vea la línea desaparecida.</para>
 */
export function useEliminarLinea() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, EliminarLineaMutationArgs>({
    mutationFn: async ({ ordenCompraId, lineaId }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/lineas/${lineaId}`,
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
