import { createContext, useContext } from 'react';
import type { ConflictDialogForm } from '@/components/erp/collaboration/ConflictResolutionDialog';

/**
 * Contexto + tipos + hook del shell-level <c>ConflictDialogProvider</c>.
 * Vive en archivo aparte del Provider para que React Fast Refresh
 * acepte la separación "componente vs no-componente" (rule
 * <c>react-refresh/only-export-components</c>).
 */

export interface OpenSimpleArgs {
  /** Callback al click en "Refrescar y revisar" — típicamente un
   * <c>queryClient.invalidateQueries(...)</c>. */
  onRefrescar: () => void | Promise<void>;
  /** TraceId del 409 para mostrar como ID de soporte. */
  traceId?: string;
}

export interface OpenPreserveArgs {
  /** Form react-hook-form (interfaz mínima: solo <c>reset</c>). */
  form: ConflictDialogForm;
  /** Capturado con <c>form.getValues()</c> antes de invalidar. */
  localValues: Record<string, unknown>;
  /** Típicamente <c>form.formState.defaultValues</c>. */
  baselineValues: Record<string, unknown>;
  /** Snapshot remoto fresco — el caller lo obtiene con
   * <c>queryClient.fetchQuery(...)</c> u observando la query
   * recién invalidada. */
  latestRemote: Record<string, unknown>;
  onRefrescar: () => void | Promise<void>;
  /** Override del default
   * <c>form.reset({ ...latestRemote, ...cambiosLocales })</c>. */
  onReaplicar?: () => void;
  /** Subset de campos relevantes (ej. excluir <c>version</c>,
   * <c>updatedAt</c>). */
  campos?: string[];
  fieldLabels?: Record<string, string>;
  traceId?: string;
}

export interface ConflictDialogApi {
  openSimple: (args: OpenSimpleArgs) => void;
  openPreserve: (args: OpenPreserveArgs) => void;
  /** Cierra el dialog programáticamente. Raro de usar; el flujo normal
   * cierra al click de los botones. */
  close: () => void;
}

export const ConflictDialogContext = createContext<ConflictDialogApi | null>(
  null,
);

/**
 * Lee el API del modal desde el contexto. Lanza si no hay provider —
 * preferimos un crash explícito en dev a un silent no-op que
 * "ignoraría" un 409 del backend.
 */
export function useConflictDialog(): ConflictDialogApi {
  const ctx = useContext(ConflictDialogContext);
  if (ctx == null) {
    throw new Error(
      'useConflictDialog() debe usarse dentro de <ConflictDialogProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
