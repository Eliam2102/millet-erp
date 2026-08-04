import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { ActualizarLineaValues } from '@/features/compras/ordenes/schemas/actualizar-linea';

/**
 * Comando wire HTTP de
 * <c>PATCH /api/v1/compras/ordenes/{id}/lineas/{lineaId}</c>. Mismo
 * shape que <c>ActualizarLineaValues</c>. PATCH parcial: campos
 * <c>null</c>/<c>undefined</c> NO se modifican; los flags
 * <c>limpiarX</c> setean a null explícitamente.
 *
 * <para>Para líneas heredadas de RQ, el backend rechaza cambios en
 * cantidad/articuloId con 422 — el editor pre-valida deshabilitando
 * inputs.</para>
 */
export type ActualizarLineaCommand = ActualizarLineaValues;

export interface ActualizarLineaMutationArgs {
  ordenCompraId: string;
  lineaId: string;
  command: ActualizarLineaCommand;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useActualizarLinea()</c> — PATCH parcial a una línea de OC.
 * Permitido en <c>Borrador</c>/<c>Rechazada</c> y solo si la línea
 * aún no tiene recepción/facturación (validación backend §4.2).
 *
 * <para>onSuccess invalida el detalle de la OC para que el editor
 * lea los nuevos valores (subtotal recalculado, IVA, etc.) en el
 * próximo render.</para>
 *
 * <para>Returns 204 No Content — la mutation devuelve <c>void</c>;
 * el caller debe re-fetchear el detalle si necesita los nuevos
 * subtotales.</para>
 */
export function useActualizarLinea() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, ActualizarLineaMutationArgs>({
    mutationFn: async ({
      ordenCompraId,
      lineaId,
      command,
      idempotencyKey,
    }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/lineas/${lineaId}`,
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
