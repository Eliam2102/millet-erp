import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { AgregarLineaManualValues } from '@/features/compras/ordenes/schemas/agregar-linea-manual';

/**
 * Comando wire HTTP de
 * <c>POST /api/v1/compras/ordenes/{id}/lineas</c> (agregar línea
 * manual). Mismo shape que <c>AgregarLineaManualValues</c> del schema
 * — el caller lo pasa directo. Sin campos derivados.
 *
 * <para><c>OrdenCompraId</c> NO está en el body; va en la URL como
 * path param. Por eso el hook recibe <c>ordenCompraId</c> separado
 * del <c>command</c>.</para>
 */
export type AgregarLineaManualCommand = AgregarLineaManualValues;

export interface AgregarLineaManualResponse {
  lineaId: string;
  posicion: number;
  /** Subtotal recalculado por el motor v0 (post-descuento). */
  subtotalLinea: number;
  /** IVA calculado por el motor v0 (16%). */
  ivaImporte: number;
}

export interface AgregarLineaManualMutationArgs {
  ordenCompraId: string;
  command: AgregarLineaManualCommand;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useAgregarLineaManual()</c> — POST a
 * <c>/{id}/lineas</c>. Solo aplica a OCs con
 * <c>sinRequisicionPrevia=true</c> (el backend valida); el editor de
 * líneas usa la matriz <c>accionAgregarLineaManual</c> para
 * gateado UI.
 *
 * <para>onSuccess invalida el detalle de la OC para que el editor
 * vea la nueva línea en la próxima render. NO invalida la bandeja
 * (las líneas no afectan el resumen del listado).</para>
 */
export function useAgregarLineaManual() {
  const queryClient = useQueryClient();

  return useMutation<
    AgregarLineaManualResponse,
    Error,
    AgregarLineaManualMutationArgs
  >({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      const { data } = await apiRequest<AgregarLineaManualResponse>(
        `/api/v1/compras/ordenes/${ordenCompraId}/lineas`,
        {
          method: 'POST',
          body: command,
          idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(ordenCompraId),
      });
    },
  });
}
