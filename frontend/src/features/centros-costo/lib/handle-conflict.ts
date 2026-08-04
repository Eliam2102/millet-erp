import type { QueryClient } from '@tanstack/react-query';
import {
  esApiError,
  esConflictoConcurrencia,
  esPrecondicionRequerida,
} from '@/lib/api';
import type { ConflictDialogApi } from '@/components/erp/collaboration/conflict-dialog-context';
import { centrosCostoKeys } from '@/features/centros-costo/api/keys';

/**
 * <c>handleCeCoMutationError(...)</c> — onError unificado de las
 * mutaciones del catálogo (CECO-FE-PR2, molde
 * <c>compras/ordenes/lib/handle-conflict.ts</c> — convención por-feature:
 * el handler decide QUÉ invalidar, y eso es del módulo).
 *
 * <para>Cubre DOS status con el mismo tratamiento (05 §6 "recargar con
 * aviso"):</para>
 * <list>
 *   <item><b>409 CONCURRENCY_CONFLICT</b> — el If-Match viajó pero la
 *   versión ya cambió (alguien editó primero).</item>
 *   <item><b>428 Precondition Required</b> — la mutación salió SIN
 *   If-Match (el ETag cacheado se perdió o nunca llegó). Se matchea por
 *   STATUS vía <c>esPrecondicionRequerida</c>: el backend NO manda
 *   <c>code</c> en el 428 (verificado contra la API viva) — un if por
 *   código aquí jamás dispararía.</item>
 * </list>
 *
 * <para>El 409 de clave duplicada (<c>CECO_CLAVE_DUPLICADA</c>) NO entra
 * aquí: significa "cambia la clave", no "recarga" — lo maneja el form
 * como error de campo.</para>
 *
 * @returns <c>true</c> si el error fue de concurrencia/precondición y ya
 * se mostró el diálogo (el caller NO muestra toast propio).
 */
export function handleCeCoMutationError(
  error: unknown,
  ctx: {
    conflictDialog: ConflictDialogApi;
    queryClient: QueryClient;
    /** Keys adicionales a invalidar además del namespace del módulo. */
    extraKeys?: readonly (readonly unknown[])[];
  },
): boolean {
  if (!esConflictoConcurrencia(error) && !esPrecondicionRequerida(error)) {
    return false;
  }

  const traceId = esApiError(error) ? error.traceId : undefined;
  ctx.conflictDialog.openSimple({
    traceId,
    onRefrescar: () => {
      // Invalidación por namespace: árbol, listas y detalles del módulo
      // se recargan (el detalle re-captura el ETag fresco).
      void ctx.queryClient.invalidateQueries({
        queryKey: centrosCostoKeys.all,
      });
      for (const key of ctx.extraKeys ?? []) {
        void ctx.queryClient.invalidateQueries({ queryKey: key });
      }
    },
  });
  return true;
}
