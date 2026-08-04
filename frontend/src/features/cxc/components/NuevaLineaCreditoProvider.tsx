import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { NuevaLineaCredito } from '@/features/cxc/pages/NuevaLineaCredito';
import {
  NuevaLineaCreditoContext,
  type NuevaLineaCreditoApi,
} from '@/features/cxc/components/nueva-linea-credito-context';

/**
 * <c>&lt;NuevaLineaCreditoProvider/&gt;</c> + Sheet de creación de línea
 * de crédito (CXC-FE-PR2). Vive a nivel shell. Espejo de
 * <c>NuevoAnticipoProvider</c>: confirm al cerrar con cambios
 * (<c>isDirty</c>); success bypasea con <c>force: true</c>.
 */
export function NuevaLineaCreditoProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
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
      'Tienes una línea de crédito sin guardar. ¿Descartar y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevaLineaCreditoApi>(
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
    <NuevaLineaCreditoContext.Provider value={api}>
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
        <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-3xl">
          <SheetHeader>
            <SheetTitle>Nueva línea de crédito</SheetTitle>
            <SheetDescription>
              Define límite, moneda, origen (SOLUNION / interno) y plazo del
              cliente. Solo puede existir una línea activa por cliente y
              moneda.
            </SheetDescription>
          </SheetHeader>

          <div className="px-2 pb-6">
            {abierto && (
              <NuevaLineaCredito
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaLineaCreditoContext.Provider>
  );
}
