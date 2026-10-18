import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type {
  TipoOperacionComercioExterior,
  TipoTransporte,
} from '@/features/compras/ordenes/api/types';

/**
 * Body del <c>PATCH /api/v1/compras/ordenes/{id}/informacion-importacion</c>
 * (F2-PR3, UF3-PR1). PATCH parcial: nullables = no tocar.
 *
 * <para>Editable solo en <c>Borrador</c>/<c>Rechazada</c>. El campo
 * <c>numeroPedimento</c> se IGNORA aquí — el backend tiene endpoint
 * dedicado <c>PATCH /numero-pedimento</c> editable post-autorización
 * sin re-auth (ver <c>useActualizarNumeroPedimento</c>). El form de
 * importación NO envía <c>numeroPedimento</c>.</para>
 */
export interface ActualizarInformacionImportacionCommand {
  incotermId?: string | null;
  paisOrigen?: string | null;
  numeroContenedor?: string | null;
  codigoRuta?: string | null;
  semanaEmbarque?: string | null;
  numeroPedimento?: string | null;
  // PLATFORM-TODO(<F1-COM-06>): supuesto Vilo, campos de comercio exterior.
  tipoOperacion?: TipoOperacionComercioExterior | null;
  numeroBl?: string | null;
  tipoTransporte?: TipoTransporte | null;
  agenteAduanalNaviera?: string | null;
  puertoAduanaEntrada?: string | null;
  /** <c>yyyy-MM-dd</c>. */
  fechaEstimadaArribo?: string | null;
  /** <c>null</c> = no tocar; <c>false</c> desmarca explícitamente. */
  certificadoOrigen?: boolean | null;
}

export interface ActualizarInformacionImportacionMutationArgs {
  ordenCompraId: string;
  command: ActualizarInformacionImportacionCommand;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useActualizarInformacionImportacion()</c> — PATCH desde el sub-tab
 * "Importación" (solo visible si <c>oc.esImportacion=true</c>).
 */
export function useActualizarInformacionImportacion() {
  const queryClient = useQueryClient();

  return useMutation<
    void,
    Error,
    ActualizarInformacionImportacionMutationArgs
  >({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/informacion-importacion`,
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
