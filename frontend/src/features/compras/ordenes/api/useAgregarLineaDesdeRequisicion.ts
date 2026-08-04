import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * Mirror del comando backend
 * <c>AgregarLineaDesdeRequisicionCommand</c> (F4-PR1, F2-PR1).
 * El cliente especifica solo la RQ a heredar — el handler valida
 * que la RQ esté Autorizada + no comprometida + misma sucursal que
 * la OC.
 */
export interface AgregarLineaDesdeRequisicionCommand {
  requisicionId: string;
}

export interface AgregarLineaDesdeRequisicionResponse {
  /** Número de líneas que se heredaron de la RQ a la OC. */
  lineasAgregadas: number;
}

export interface AgregarLineaDesdeRequisicionMutationArgs {
  ordenCompraId: string;
  command: AgregarLineaDesdeRequisicionCommand;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useAgregarLineaDesdeRequisicion()</c> — mutation para
 * <c>POST /api/v1/compras/ordenes/{id}/lineas/desde-requisicion</c>.
 *
 * <para>Usado por el flujo de <b>consolidación N:1</b> del Sheet
 * "Nueva OC" (UF2-PR1 + UF2-PR2): después de crear la OC vacía, el
 * caller itera sobre las RQs seleccionadas en
 * <c>&lt;SelectorRequisicionesConsolidacion/&gt;</c> y llama este hook
 * para cada una. Cada call necesita su propio Idempotency-Key para
 * que un retry no agregue líneas duplicadas.</para>
 *
 * <para>Errores comunes a manejar en el caller:</para>
 * <list>
 *   <item><c>404 OC_NO_ENCONTRADA / RQ_NO_ENCONTRADA</c></item>
 *   <item><c>422 RQ_NO_AUTORIZADA / RQ_SUCURSAL_DISTINTA</c> —
 *   restricción §10.5 cerrado.</item>
 *   <item><c>409 RQ_YA_COMPROMETIDA</c> — alguien más la comprometió
 *   entre el fetch del selector y el submit (carrera).</item>
 * </list>
 *
 * <para>NO invalida queries en onSuccess porque el caller orquesta
 * las invalidaciones al final del lote (después del último RQ
 * agregado, invalida <c>ordenesKeys.detail(ocId)</c> +
 * <c>requisiciones</c> para reflejar las RQs comprometidas).</para>
 */
export function useAgregarLineaDesdeRequisicion() {
  const queryClient = useQueryClient();
  void queryClient; // reservado para extensiones futuras

  return useMutation<
    AgregarLineaDesdeRequisicionResponse,
    Error,
    AgregarLineaDesdeRequisicionMutationArgs
  >({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      const { data } = await apiRequest<AgregarLineaDesdeRequisicionResponse>(
        `/api/v1/compras/ordenes/${ordenCompraId}/lineas/desde-requisicion`,
        {
          method: 'POST',
          body: command,
          idempotencyKey,
        },
      );
      return data;
    },
  });
}
