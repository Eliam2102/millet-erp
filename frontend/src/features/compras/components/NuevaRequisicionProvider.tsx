import { ContextoSucursal } from '@/components/erp/display/ContextoSucursal';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
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
 * un diálogo DS (<c>useConfirmarDescarte</c>) antes de descartar. El flujo de éxito
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
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(
    alCerrar,
    'Tu borrador queda guardado localmente; la próxima vez te lo ofreceremos recuperar.',
  );
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevaRequisicionApi>(
    () => ({
      abrir: () => {
        setDirty(false);
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
            <ContextoSucursal />
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
      {dialogo}
    </NuevaRequisicionContext.Provider>
  );
}
