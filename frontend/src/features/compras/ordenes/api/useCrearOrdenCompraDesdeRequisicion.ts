import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * Mirror del comando backend
 * <c>CrearOrdenCompraDesdeRequisicionCommand</c>. La cabecera de la OC
 * hereda datos de la RQ origen (sucursal/almacén), pero el comprador
 * aún elige proveedor + condiciones de pago + uso principal + fecha
 * + moneda + flag de importación + observaciones.
 *
 * <para>El backend valida que la RQ esté <c>Autorizada</c> y que NO
 * haya sido comprometida ya en otra OC; marca la RQ como
 * <c>ComprometidaEnOcId</c> en la misma TX y publica
 * <c>RqComprometidaEnOcEvent</c>.</para>
 */
export interface CrearOrdenCompraDesdeRequisicionCommand {
  requisicionId: string;
  sucursalCodigo: string;
  folioAnio: number;
  proveedorId: string;
  condicionesPagoId: string;
  usoPrincipalId: string;
  /** ISO 8601 UTC. */
  fechaDocumento: string;
  moneda?: string;
  tipoCambio?: number | null;
  esImportacion?: boolean;
  /** ISO 8601 UTC. */
  fechaEntregaEsperada?: string | null;
  observaciones?: string | null;
}

/**
 * Respuesta de <c>POST /api/v1/compras/ordenes/desde-requisicion</c>.
 * Notar: incluye <c>lineasHeredadas</c> (count) — el comprador puede
 * verificarlo en el toast de éxito antes de navegar al detalle.
 */
export interface CrearOrdenCompraDesdeRequisicionResponse {
  ordenCompraId: string;
  folio: string;
  folioAnio: number;
  lineasHeredadas: number;
}

export interface CrearOrdenCompraDesdeRequisicionMutationArgs {
  command: CrearOrdenCompraDesdeRequisicionCommand;
  /** UUID v4 estable durante la vida del componente — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useCrearOrdenCompraDesdeRequisicion()</c> — mutation hook para el
 * modo 1:1 del Sheet "Nueva OC". Convierte una RQ Autorizada en una
 * OC Borrador heredando líneas vía FK
 * (<c>RequisicionId, LineaRequisicionId</c>).
 *
 * <para>Errores comunes a manejar en el caller:</para>
 * <list>
 *   <item><c>404 RQ_NO_ENCONTRADA</c>: la RQ ya no existe o no es
 *   accesible para la empresa actual.</item>
 *   <item><c>422 RQ_NO_AUTORIZADA</c>: la RQ no está en estado
 *   Autorizada. UI debería filtrar las RQs no-autorizadas en el
 *   selector, pero defensa en profundidad.</item>
 *   <item><c>409 RQ_YA_COMPROMETIDA</c>: alguien más ya creó una OC
 *   desde esta RQ entre el momento del fetch y el submit (carrera).</item>
 * </list>
 */
export function useCrearOrdenCompraDesdeRequisicion() {
  const queryClient = useQueryClient();

  return useMutation<
    CrearOrdenCompraDesdeRequisicionResponse,
    Error,
    CrearOrdenCompraDesdeRequisicionMutationArgs
  >({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<
        CrearOrdenCompraDesdeRequisicionResponse
      >('/api/v1/compras/ordenes/desde-requisicion', {
        method: 'POST',
        body: command,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ordenesKeys.all });
      // También invalidar RQs porque la origen pasa a estar
      // "comprometida" y debe reflejarse en su detalle/bandeja.
      queryClient.invalidateQueries({ queryKey: ['compras', 'requisiciones'] });
    },
  });
}
