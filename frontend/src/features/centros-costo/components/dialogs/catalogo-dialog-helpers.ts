import type { QueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { applyServerErrors, esApiError } from '@/lib/api';
import type { FormConSetError } from '@/lib/api/apply-server-errors';
import type { ConflictDialogApi } from '@/components/erp/collaboration/conflict-dialog-context';
import { handleCeCoMutationError } from '@/features/centros-costo/lib/handle-conflict';

/**
 * onError compartido de los modales del catálogo (el "wiring repetido"
 * que el shell evita copiar 5 veces). Orden de resolución:
 *
 * 1. Códigos de UNICIDAD → error inline en el campo (significan "cambia
 *    el valor", no "recarga": el 409 de clave duplicada NUNCA va al
 *    diálogo de conflicto).
 * 2. 409 concurrencia / 428 sin If-Match → ConflictResolutionDialog con
 *    recarga (handleCeCoMutationError; el 428 se matchea por STATUS).
 * 3. 422 con errores[] → applyServerErrors a campos.
 * 4. Resto → toast con título del problem y traceId.
 */
export function manejarErrorDialogoCatalogo(
  error: unknown,
  ctx: {
    /** El caller castea su UseFormReturn (idiom del molde: los field
     * names de RHF son más estrictos que string). */
    form: FormConSetError;
    /** Campo que recibe el error de unicidad (clave o nombre). */
    campoUnicidad: 'clave' | 'nombre';
    conflictDialog: ConflictDialogApi;
    queryClient: QueryClient;
  },
): void {
  if (esApiError(error)) {
    if (
      error.code === 'CECO_CLAVE_DUPLICADA' ||
      error.code === 'CECO_GRUPO_DIM2_NOMBRE_DUPLICADO' ||
      error.code === 'CECO_GRUPO_DIM3_NOMBRE_DUPLICADO'
    ) {
      ctx.form.setError(ctx.campoUnicidad, {
        type: error.code,
        message: error.problem.detail ?? error.problem.title,
      });
      return;
    }

    if (
      handleCeCoMutationError(error, {
        conflictDialog: ctx.conflictDialog,
        queryClient: ctx.queryClient,
      })
    ) {
      return;
    }

    if (applyServerErrors(ctx.form, error)) {
      return;
    }

    toast.error(error.problem.title, {
      description: error.traceId ? `Código: ${error.traceId}` : undefined,
    });
    return;
  }

  toast.error('Error inesperado en el catálogo de centros de costo.');
}
