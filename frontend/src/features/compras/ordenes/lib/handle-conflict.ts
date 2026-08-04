import type { QueryClient } from '@tanstack/react-query';
import { esApiError } from '@/lib/api';
import { esConflictoConcurrencia } from '@/lib/api/error';
import type { ConflictDialogApi } from '@/components/erp/collaboration/conflict-dialog-context';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * <c>handleOcMutationError(...)</c> — helper unificado para el onError
 * de cualquier mutation de OC (UF4-PR2). Centraliza el wireup del
 * <c>&lt;ConflictResolutionDialog/&gt;</c> ante 409 + el toast genérico
 * para el resto de errores.
 *
 * <para><b>Comportamiento</b>:</para>
 * <list>
 *   <item>409 (concurrencia) → abre el dialog en modo simple
 *   ("refrescar y revisar") con <c>onRefrescar</c> que invalida el
 *   detail de la OC. El caller recibe <c>true</c> y NO muestra toast
 *   propio.</item>
 *   <item>Otros errores → caller maneja como antes (typically toast +
 *   applyServerErrors si aplica). Devuelve <c>false</c>.</item>
 * </list>
 *
 * <para>Para "preserve mode" (forms largos como SheetNuevaOC), usar
 * <c>handleOcMutationErrorPreserve(...)</c> en lugar de éste.</para>
 */
export function handleOcMutationError(
  error: unknown,
  ctx: {
    ordenCompraId: string;
    conflictDialog: ConflictDialogApi;
    queryClient: QueryClient;
  },
): boolean {
  if (!esConflictoConcurrencia(error)) {
    return false;
  }
  const traceId = esApiError(error) ? error.traceId : undefined;
  ctx.conflictDialog.openSimple({
    traceId,
    onRefrescar: () => {
      void ctx.queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(ctx.ordenCompraId),
      });
    },
  });
  return true;
}
