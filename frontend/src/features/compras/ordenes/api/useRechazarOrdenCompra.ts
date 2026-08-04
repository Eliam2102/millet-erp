import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { RechazarOcValues } from '@/features/compras/ordenes/schemas/rechazar';

export interface RechazarOrdenCompraMutationArgs {
  ordenCompraId: string;
  command: RechazarOcValues;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useRechazarOrdenCompra()</c> — POST <c>/{id}/rechazar</c>
 * (UF4-PR1). Rechaza una OC en <c>EnAutorizacionJefeCompras</c> o
 * <c>EnAutorizacionDireccion</c>. Backend valida permiso según el
 * estado actual y exige texto si el motivo tiene
 * <c>permiteTextoLibre=true</c>. La OC transiciona a
 * <c>Rechazada</c>; sigue editable y se puede re-transmitir.
 */
export function useRechazarOrdenCompra() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, RechazarOrdenCompraMutationArgs>({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/rechazar`,
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
