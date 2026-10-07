import type { QueryClient } from '@tanstack/react-query';
import { applyServerErrors, esApiError, esConflictoConcurrencia, esPrecondicionRequerida } from '@/lib/api';
import type { FormConSetError } from '@/lib/api/apply-server-errors';
import type { ConflictDialogApi } from '@/components/erp/collaboration/conflict-dialog-context';
import { contabKeys } from '../api/hooks';

/** Código de negocio del backend (422/409) → campo del formulario que lo causa. */
const CAMPO_POR_CODIGO: Record<string, string> = {
  CONTAB_CUENTA_CODIGO_INVALIDO: 'codigo',
  CONTAB_CUENTA_CODIGO_DUPLICADO: 'codigo',
  CONTAB_CUENTA_CODIGO_FUERA_DE_RAMA: 'codigo',
  CONTAB_CUENTA_NOMBRE_INVALIDO: 'nombre',
  CONTAB_CUENTA_PADRE_INVALIDO: 'padreId',
  CONTAB_CUENTA_PADRE_NO_ES_TITULO: 'padreId',
  CONTAB_CUENTA_CICLO: 'padreId',
  CONTAB_CUENTA_NIVEL_EXCEDIDO: 'padreId',
  CONTAB_CUENTA_NATURALEZA_INVALIDA: 'naturaleza',
  CONTAB_CUENTA_AFECTABLE_CON_HIJAS: 'tipo',
  CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE: 'cuentaControl',
  CONTAB_CUENTA_CONTROL_CONFLICTO: 'cuentaControl',
};

export interface ContextoError {
  /** Formulario al que se asignan los errores de campo (opcional). */
  form?: FormConSetError;
  conflictDialog: ConflictDialogApi;
  queryClient: QueryClient;
  /** Mensaje a mostrar a nivel de formulario (negocio sin campo, 403, fallo recuperable). */
  setMensaje: (mensaje: string) => void;
}

/**
 * onError unificado de las mutaciones de Contabilidad. Orden:
 * 409 CONCURRENCY_CONFLICT / 428 → diálogo "recargar" (nunca sobrescribe; el formulario conserva el borrador);
 * errores[] / código con campo → error en el campo; 403 → sin permiso; negocio → mensaje del servidor;
 * cualquier otro (red, 5xx) → fallo recuperable: el usuario reintenta sin perder datos.
 */
export function manejarErrorCuenta(error: unknown, ctx: ContextoError): void {
  if (esConflictoConcurrencia(error) || esPrecondicionRequerida(error)) {
    ctx.conflictDialog.openSimple({
      traceId: esApiError(error) ? error.traceId : undefined,
      onRefrescar: () => void ctx.queryClient.invalidateQueries({ queryKey: contabKeys.all }),
    });
    return;
  }
  if (!esApiError(error)) {
    ctx.setMensaje('No se pudo completar la operación (sin conexión o error inesperado). Tus datos se conservan: reintenta.');
    return;
  }
  if (error.status === 403) {
    ctx.setMensaje('No tienes permiso para esta acción.');
    return;
  }
  if (ctx.form && applyServerErrors(ctx.form, error)) return;
  const campo = error.code ? CAMPO_POR_CODIGO[error.code] : undefined;
  const detalle = error.problem.detail ?? error.problem.title;
  if (campo && ctx.form) {
    ctx.form.setError(campo, { type: error.code!, message: detalle });
    ctx.setMensaje(detalle);
    return;
  }
  if (error.status >= 500) {
    ctx.setMensaje(`El servidor no pudo procesar la solicitud${error.traceId ? ` (código ${error.traceId})` : ''}. Tus datos se conservan: reintenta.`);
    return;
  }
  ctx.setMensaje(detalle);
}
