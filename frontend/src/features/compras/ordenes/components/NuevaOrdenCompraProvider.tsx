import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { SheetNuevaOC } from '@/features/compras/ordenes/pages/SheetNuevaOC';
import {
  NuevaOrdenCompraContext,
  type DesdeRequisicionContext,
  type NuevaOrdenCompraApi,
} from '@/features/compras/ordenes/components/nueva-orden-compra-context';

/**
 * <c>&lt;NuevaOrdenCompraProvider/&gt;</c> + el Sheet asociado. Vive a
 * nivel shell (<c>routes/_app.tsx</c>) para que cualquier botón
 * "Nueva OC" del módulo Compras dispare el mismo overlay. Mismo
 * patrón cross-módulo que <c>NuevaRequisicionProvider</c>.
 *
 * <para>Sheet slide-from-right (max-w-3xl). El form dentro es
 * <c>&lt;SheetNuevaOC/&gt;</c> con <c>onClose</c> + <c>onDirtyChange</c>
 * para coordinar el confirm-on-close cuando hay cambios sin guardar.
 * El draft persistido en localStorage sobrevive de todos modos — el
 * confirm es un freno extra para evitar perder cambios que aún no
 * llegaron al debounce de 500ms.</para>
 */
export function NuevaOrdenCompraProvider({
  children,
}: {
  children: ReactNode;
}) {
  const [abierto, setAbierto] = useState(false);
  const [desdeRequisicion, setDesdeRequisicion] =
    useState<DesdeRequisicionContext | null>(null);
  const isDirtyRef = useRef(false);

  const setDirty = useCallback((dirty: boolean) => {
    isDirtyRef.current = dirty;
  }, []);

  const cerrarConConfirm = useCallback((opts?: { force?: boolean }) => {
    if (opts?.force === true || !isDirtyRef.current) {
      isDirtyRef.current = false;
      setAbierto(false);
      setDesdeRequisicion(null);
      return;
    }
    const confirmar = window.confirm(
      'Tienes cambios sin guardar en la nueva OC. ¿Descartarlos y cerrar?\n\n(Tu borrador queda guardado localmente; la próxima vez te lo ofreceremos recuperar.)',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
      setDesdeRequisicion(null);
    }
  }, []);

  const api = useMemo<NuevaOrdenCompraApi>(
    () => ({
      abrir: (opts) => {
        isDirtyRef.current = false;
        setDesdeRequisicion(opts?.desdeRequisicion ?? null);
        setAbierto(true);
      },
      cerrar: cerrarConConfirm,
      setDirty,
    }),
    [cerrarConConfirm, setDirty],
  );

  return (
    <NuevaOrdenCompraContext.Provider value={api}>
      {children}
      <Sheet
        open={abierto}
        onOpenChange={(open) => {
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
            <SheetTitle>
              {desdeRequisicion
                ? `Convertir RQ ${desdeRequisicion.folio ?? ''} a OC`.trim()
                : 'Nueva orden de compra'}
            </SheetTitle>
            <SheetDescription>
              {desdeRequisicion
                ? 'Modo 1:1. La sucursal viene de la RQ origen. Llena los campos restantes (proveedor, condiciones de pago, etc.).'
                : 'Llena la cabecera. Las líneas se agregan después de crear, dentro del detalle (UF2-PR3 cableará el editor inline).'}
            </SheetDescription>
          </SheetHeader>

          <div className="px-6 pb-6">
            {abierto && (
              <SheetNuevaOC
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
                desdeRequisicion={desdeRequisicion}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaOrdenCompraContext.Provider>
  );
}
