import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  NuevaCajaContext,
  type NuevaCajaApi,
} from '@/features/facturacion/components/nueva-caja-context';
import { NuevaCaja } from '@/features/facturacion/pages/NuevaCaja';
import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';

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
        <SheetContent side="right" size="lg">
          <SheetHeader>
            <SheetTitle>Nueva caja</SheetTitle>
            <SheetDescription>
              La caja nace activa en la sucursal que elijas; asigna canales, cajeros y más
              sucursales desde su detalle.
            </SheetDescription>
          </SheetHeader>
          <SheetBody>
            <div className="min-w-0">
              {abierto && <NuevaCaja onClose={cerrarConConfirm} onDirtyChange={setDirty} />}
            </div>
          </SheetBody>
        </SheetContent>
      </Sheet>
    </NuevaCajaContext.Provider>
  );
}
