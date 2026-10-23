import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * Body del <c>PATCH /api/v1/compras/ordenes/{id}</c> (cabecera) filtrado a
 * proveedor, condiciones de pago y uso principal (UX-F0-01). Solo se envían
 * los campos presentes: el PATCH del backend trata <c>null</c>/ausente como
 * "no tocar".
 */
export interface ActualizarCabeceraOcCommand {
  proveedorId?: string;
  condicionesPagoId?: string;
  usoPrincipalId?: string;
}

export interface ActualizarCabeceraOcMutationArgs {
  ordenCompraId: string;
  command: ActualizarCabeceraOcCommand;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/** <c>useActualizarCabeceraOc()</c> — PATCH parcial de la cabecera de la OC. */
export function useActualizarCabeceraOc() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, ActualizarCabeceraOcMutationArgs>({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      const body: ActualizarCabeceraOcCommand = {};
      if (command.proveedorId !== undefined) body.proveedorId = command.proveedorId;
      if (command.condicionesPagoId !== undefined) body.condicionesPagoId = command.condicionesPagoId;
      if (command.usoPrincipalId !== undefined) body.usoPrincipalId = command.usoPrincipalId;
      await apiRequest<void>(`/api/v1/compras/ordenes/${ordenCompraId}`, {
        method: 'PATCH',
        body,
        idempotencyKey,
      });
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(ordenCompraId),
      });
    },
  });
}
