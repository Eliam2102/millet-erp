import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { NuevaPropuestaAplicacion } from '@/features/cxc/pages/NuevaPropuestaAplicacion';
import {
  NuevaPropuestaContext,
  type NuevaPropuestaApi,
} from '@/features/cxc/components/nueva-propuesta-context';

/**
 * <c>&lt;NuevaPropuestaProvider/&gt;</c> + Sheet "Nueva propuesta de
 * aplicación" (CXC-FE-PR6). Vive a nivel shell. Espejo de
 * <c>RegistrarGestionProvider</c>.
 */
export function NuevaPropuestaProvider({ children }: { children: ReactNode }) {
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
      'Tienes una propuesta sin proponer. ¿Descartar y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevaPropuestaApi>(
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
    <NuevaPropuestaContext.Provider value={api}>
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
        <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-4xl">
          <SheetHeader>
            <SheetTitle>Nueva propuesta de aplicación</SheetTitle>
            <SheetDescription>
              Matching depósito ↔ facturas desde el remittance del cliente.
              CxC propone; Ingresos confirma o rechaza.
            </SheetDescription>
          </SheetHeader>

          <div className="px-2 pb-6">
            {abierto && (
              <NuevaPropuestaAplicacion
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaPropuestaContext.Provider>
  );
}
