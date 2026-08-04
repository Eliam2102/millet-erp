import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { NuevaRequisicion } from '@/features/compras/pages/NuevaRequisicion';
import {
  NuevaRequisicionContext,
  type NuevaRequisicionApi,
} from '@/features/compras/components/nueva-requisicion-context';

/**
 * <c>&lt;NuevaRequisicionProvider/&gt;</c> + el Sheet asociado. Vive a
 * nivel shell (<c>routes/_app.tsx</c>) para que cualquier botón
 * "Nueva" del módulo Compras dispare el mismo overlay.
 *
 * <para>Diseño polish: Sheet slide-from-right (max-w-3xl) en lugar
 * de página full-screen <c>/compras/requisiciones/nueva</c>. El form
 * dentro es el mismo componente <c>&lt;NuevaRequisicion/&gt;</c> con
 * prop <c>onClose</c> activada — así se reusa toda la lógica
 * (validación, draft persistido, recovery modal, conflict dialog).</para>
 *
 * <para><b>Confirm al cerrar con cambios sin guardar</b>: si el form
 * reportó <c>isDirty=true</c> via <c>setDirty</c>, los gestos de
 * cerrar (Esc, click fuera, botón X del Sheet, X de la app) muestran
 * un <c>window.confirm</c> antes de descartar. El flujo de éxito
 * (post-submit) llama <c>cerrar({ force: true })</c> para saltar el
 * confirm. El draft persistido en localStorage sobrevive de todos
 * modos — el confirm es un freno extra para evitar perder cambios
 * que aún no llegaron al debounce de 500ms del persistir.</para>
 */
export function NuevaRequisicionProvider({
  children,
}: {
  children: ReactNode;
}) {
  const [abierto, setAbierto] = useState(false);
  // <c>isDirtyRef</c> en ref + state — el ref lo leen los handlers
  // (sin re-render), el state forzará el re-render que limpia el flag
  // al cerrar.
  const isDirtyRef = useRef(false);

  const setDirty = useCallback((dirty: boolean) => {
    isDirtyRef.current = dirty;
  }, []);

  const cerrarConConfirm = useCallback((opts?: { force?: boolean }) => {
    if (opts?.force === true || !isDirtyRef.current) {
      isDirtyRef.current = false;
      setAbierto(false);
      return;
    }
    const confirmar = window.confirm(
      'Tienes cambios sin guardar en la nueva requisición. ¿Descartarlos y cerrar?\n\n(Tu borrador queda guardado localmente; la próxima vez te lo ofreceremos recuperar.)',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevaRequisicionApi>(
    () => ({
      abrir: () => {
        isDirtyRef.current = false;
        setAbierto(true);
      },
      cerrar: cerrarConConfirm,
      setDirty,
    }),
    [cerrarConConfirm, setDirty],
  );

  return (
    <NuevaRequisicionContext.Provider value={api}>
      {children}
      <Sheet
        open={abierto}
        onOpenChange={(open) => {
          // Cuando Radix Dialog pide cerrar (open=false: Esc, click
          // fuera, botón X interno), aplicamos el guard de dirty. El
          // open=true lo disparamos solo desde <c>abrir()</c>, así que
          // acá ignoramos esa rama.
          if (!open) {
            cerrarConConfirm();
            return;
          }
          setAbierto(true);
        }}
      >
        <SheetContent
          side="right"
          className="w-full overflow-y-auto sm:max-w-3xl"
        >
          <SheetHeader>
            <SheetTitle>Nueva requisición</SheetTitle>
            <SheetDescription>
              Llena la cabecera. Las líneas se agregan después de crear,
              dentro del detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <NuevaRequisicion
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaRequisicionContext.Provider>
  );
}
