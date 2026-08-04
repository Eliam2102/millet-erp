import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { DescuentoTipo } from '@/features/compras/ordenes/api/types';

/**
 * Body del <c>PATCH /api/v1/compras/ordenes/{id}</c> (cabecera) filtrado
 * a los 4 campos de totales financieros (UF3-PR1). El backend no expone
 * un endpoint dedicado de "totales-financieros" — los 4 campos viven en
 * la cabecera y se editan vía PATCH /{id}.
 *
 * <para>Para limpiar el descuento global (poner a null) se envía
 * <c>limpiarDescuentoGlobal: true</c> en lugar de mandar
 * <c>descuentoGlobalTipo/Valor</c> en null (PATCH nullable = no tocar).</para>
 *
 * <para>Editable solo en <c>Borrador</c>/<c>Rechazada</c>. Requiere
 * <c>Idempotency-Key</c> (el endpoint cabecera lo exige porque la
 * actualización dispara reevaluación de impuestos en líneas).</para>
 */
export interface ActualizarTotalesFinancierosCommand {
  descuentoGlobalTipo?: DescuentoTipo | null;
  descuentoGlobalValor?: number | null;
  gastosAdicionales?: number | null;
  redondeo?: number | null;
  limpiarDescuentoGlobal?: boolean;
}

export interface ActualizarTotalesFinancierosMutationArgs {
  ordenCompraId: string;
  command: ActualizarTotalesFinancierosCommand;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useActualizarTotalesFinancieros()</c> — PATCH parcial sobre la
 * cabecera, mandando solo los 4 campos de totales financieros.
 */
export function useActualizarTotalesFinancieros() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, ActualizarTotalesFinancierosMutationArgs>({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/compras/ordenes/${ordenCompraId}`, {
        method: 'PATCH',
        body: command,
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
