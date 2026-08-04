import { useMemo, useState, type ReactNode } from 'react';
import { ConflictResolutionDialog } from '@/components/erp/collaboration/ConflictResolutionDialog';
import {
  ConflictDialogContext,
  type ConflictDialogApi,
  type OpenSimpleArgs,
  type OpenPreserveArgs,
} from '@/components/erp/collaboration/conflict-dialog-context';

/**
 * <c>&lt;ConflictDialogProvider/&gt;</c> — shell-level singleton del
 * <c>&lt;ConflictResolutionDialog/&gt;</c>. Cualquier hook de mutation
 * o call site que detecte un <c>409 CONCURRENCY_CONFLICT</c> abre el
 * modal vía <c>useConflictDialog()</c>; se monta una sola vez en
 * <c>routes/_app.tsx</c> (UF3-PR3, ADR-0032 patrón shell).
 *
 * <para>Doc 05 §8.4 (modo preserve) y §8.5 (modo simple) describen los
 * dos flujos. Esta API es el "wireup" mencionado en F9 Rev. 3.</para>
 *
 * <para><b>Modo simple</b> — acciones sin form (transmitir, aprobar,
 * rechazar, eliminar línea, cancelar). El caller pasa solo
 * <c>onRefrescar</c> y opcional <c>traceId</c>.</para>
 *
 * <para><b>Modo preserve</b> — mutations desde formularios. El caller
 * captura <c>localValues = form.getValues()</c> ANTES de invalidar la
 * query (doc 05 §8.4), refetcha el latest remote, y abre el modal con
 * los snapshots. El dialog calcula el diff, ofrece "Reaplicar mis
 * cambios" o "Solo refrescar".</para>
 *
 * <para>El hook <c>useConflictDialog()</c> y los tipos viven en
 * <c>conflict-dialog-context.ts</c> (separación requerida por
 * <c>react-refresh/only-export-components</c>).</para>
 */

interface SimpleState extends OpenSimpleArgs {
  mode: 'simple';
  open: true;
}

interface PreserveState extends OpenPreserveArgs {
  mode: 'preserve';
  open: true;
}

type DialogState =
  | SimpleState
  | PreserveState
  | { mode: null; open: false };

const CLOSED: DialogState = { mode: null, open: false };

export function ConflictDialogProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<DialogState>(CLOSED);

  const api = useMemo<ConflictDialogApi>(
    () => ({
      openSimple: (args) =>
        setState({ mode: 'simple', open: true, ...args }),
      openPreserve: (args) =>
        setState({ mode: 'preserve', open: true, ...args }),
      close: () => setState(CLOSED),
    }),
    [],
  );

  function handleOpenChange(open: boolean) {
    if (!open) setState(CLOSED);
  }

  function handleRefrescar() {
    if (state.mode == null) return;
    void state.onRefrescar();
    setState(CLOSED);
  }

  return (
    <ConflictDialogContext.Provider value={api}>
      {children}
      <ConflictResolutionDialog
        open={state.open}
        onOpenChange={handleOpenChange}
        onRefrescar={handleRefrescar}
        traceId={state.mode != null ? state.traceId : undefined}
        form={state.mode === 'preserve' ? state.form : undefined}
        localValues={
          state.mode === 'preserve' ? state.localValues : undefined
        }
        baselineValues={
          state.mode === 'preserve' ? state.baselineValues : undefined
        }
        latestRemote={
          state.mode === 'preserve' ? state.latestRemote : undefined
        }
        onReaplicar={
          state.mode === 'preserve' ? state.onReaplicar : undefined
        }
        campos={state.mode === 'preserve' ? state.campos : undefined}
        fieldLabels={
          state.mode === 'preserve' ? state.fieldLabels : undefined
        }
      />
    </ConflictDialogContext.Provider>
  );
}
