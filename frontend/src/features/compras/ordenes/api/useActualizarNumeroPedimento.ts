import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * Body del <c>PATCH /api/v1/compras/ordenes/{id}/numero-pedimento</c>
 * (permiso <c>compras.ordenes.logistica</c>, editable post-autorización).
 *
 * <para>El backend REEMPLAZA los cuatro campos: un <c>null</c> limpia el
 * valor. El caller debe enviar siempre el estado completo de los cuatro.</para>
 */
// PLATFORM-TODO(<F1-COM-06>): supuesto Vilo, campos de comercio exterior.
export interface ActualizarNumeroPedimentoCommand {
  numeroPedimento: string | null;
  numeroBl: string | null;
  /** <c>yyyy-MM-dd</c>. */
  fechaEstimadaArribo: string | null;
  agenteAduanalNaviera: string | null;
}

export interface ActualizarNumeroPedimentoMutationArgs {
  ordenCompraId: string;
  command: ActualizarNumeroPedimentoCommand;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useActualizarNumeroPedimento()</c> — control de pedimento, BL, ETA y
 * agente aduanal de una OC de comercio exterior. Disponible desde
 * Borrador hasta antes de Cerrada/Cancelada (sin re-autorización).
 */
export function useActualizarNumeroPedimento() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, ActualizarNumeroPedimentoMutationArgs>({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/numero-pedimento`,
        {
          method: 'PATCH',
          body: command,
          idempotencyKey,
        },
      );
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(ordenCompraId),
      });
    },
  });
}
