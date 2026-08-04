import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { DuplicarOcValues } from '@/features/compras/ordenes/schemas/duplicar';

/**
 * Mirror del response backend <c>DuplicarOrdenCompraResponse</c>
 * (UF5-PR2, F6-PR2).
 */
export interface DuplicarOrdenCompraResponse {
  ordenCompraNuevaId: string;
  folioNuevo: string;
  ordenCompraOrigenId: string;
  folioOrigen: string;
}

export interface DuplicarOrdenCompraMutationArgs {
  ordenCompraOrigenId: string;
  command: DuplicarOcValues;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useDuplicarOrdenCompra()</c> — POST <c>/{id}/duplicar</c>
 * (UF5-PR2, F6-PR2). Duplica una OC <c>Cancelada</c> o <c>Rechazada</c>
 * como una OC nueva en <c>Borrador</c>. Hereda cabecera + líneas como
 * manuales (sin FK a RQ — el comprador re-selecciona). NO copia:
 * adjuntos, autorizaciones, sub-estados, motivos de rechazo/cancelación,
 * vínculos a RQs originales.
 *
 * <para>Setea <c>OcOrigenId</c> en la nueva OC para trazabilidad.
 * El caller redirige a P3 de la nueva OC tras éxito.</para>
 */
export function useDuplicarOrdenCompra() {
  const queryClient = useQueryClient();

  return useMutation<
    DuplicarOrdenCompraResponse,
    Error,
    DuplicarOrdenCompraMutationArgs
  >({
    mutationFn: async ({ ordenCompraOrigenId, command, idempotencyKey }) => {
      const { data } = await apiRequest<DuplicarOrdenCompraResponse>(
        `/api/v1/compras/ordenes/${ordenCompraOrigenId}/duplicar`,
        {
          method: 'POST',
          body: command,
          idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      // La OC origen sigue igual (Cancelada/Rechazada). La nueva entra
      // en Borrador. Invalidamos toda la familia para que la bandeja
      // muestre la nueva.
      queryClient.invalidateQueries({ queryKey: ordenesKeys.all });
    },
  });
}
