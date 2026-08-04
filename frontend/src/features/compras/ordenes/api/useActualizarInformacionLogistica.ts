import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * Body del <c>PATCH /api/v1/compras/ordenes/{id}/informacion-logistica</c>
 * (F2-PR3, UF3-PR1). PATCH parcial: nullables = no tocar; el backend
 * mantiene los valores anteriores en los campos no enviados.
 *
 * <para>Editable hasta <c>Autorizada</c> inclusive (permiso
 * <c>compras.ordenes.logistica</c> permite edición post-autorización).
 * El número de guía y contenedor cambian durante el ciclo de recepción
 * sin re-autorización.</para>
 */
export interface ActualizarInformacionLogisticaCommand {
  direccionEntrega?: string | null;
  transportistaId?: string | null;
  transportistaTexto?: string | null;
  numeroGuia?: string | null;
  instruccionesEnvio?: string | null;
}

export interface ActualizarInformacionLogisticaMutationArgs {
  ordenCompraId: string;
  command: ActualizarInformacionLogisticaCommand;
  /** UUID v4 estable por intento — ADR-0020. Backend lo requiere. */
  idempotencyKey: string;
}

/**
 * <c>useActualizarInformacionLogistica()</c> — PATCH inline desde el
 * sub-tab "Logística". onSuccess invalida el detalle para reflejar los
 * cambios. NO invalida la bandeja (campos no listados ahí).
 */
export function useActualizarInformacionLogistica() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, ActualizarInformacionLogisticaMutationArgs>({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/informacion-logistica`,
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
