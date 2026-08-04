import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { NuevaCaja } from '@/features/facturacion/pages/NuevaCaja';
import {
  NuevaCajaContext,
  type NuevaCajaApi,
} from '@/features/facturacion/components/nueva-caja-context';

/**
 * <c>&lt;NuevaCajaProvider/&gt;</c> + Sheet de alta de caja (CAJAS-PR5).
 * Vive a nivel shell. Espejo de <c>NuevoReppProvider</c>.
 */
export function NuevaCajaProvider({ children }: { children: ReactNode }) {
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
    const confirmar = window.confirm('Tienes una caja sin guardar. ¿Descartar y cerrar?');
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevaCajaApi>(
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
    <NuevaCajaContext.Provider value={api}>
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
        <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-xl">
          <SheetHeader>
            <SheetTitle>Nueva caja</SheetTitle>
            <SheetDescription>
              La caja nace activa y sin alcance (todas las sucursales y canales);
              asigna sucursales, canales y cajeros desde su detalle.
            </SheetDescription>
          </SheetHeader>

          <div className="px-2 pb-6">
            {abierto && <NuevaCaja onClose={cerrarConConfirm} onDirtyChange={setDirty} />}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaCajaContext.Provider>
  );
}
