import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { NuevoRepp } from '@/features/facturacion/pages/NuevoRepp';
import {
  NuevoReppContext,
  type NuevoReppApi,
} from '@/features/facturacion/components/nuevo-repp-context';

/**
 * <c>&lt;NuevoReppProvider/&gt;</c> + Sheet de emisión de REPP (FE-F6).
 * Vive a nivel shell. Espejo de <c>NuevoAnticipoProvider</c>.
 */
export function NuevoReppProvider({ children }: { children: ReactNode }) {
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
      'Tienes un complemento de pago sin emitir. ¿Descartar y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoReppApi>(
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
    <NuevoReppContext.Provider value={api}>
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
            <SheetTitle>Nuevo complemento de pago (REPP)</SheetTitle>
            <SheetDescription>
              Pago 2.0 multi-factura. Captura el pago y las facturas que cubre;
              todas deben ser del mismo receptor.
            </SheetDescription>
          </SheetHeader>

          <div className="px-2 pb-6">
            {abierto && (
              <NuevoRepp onClose={cerrarConConfirm} onDirtyChange={setDirty} />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoReppContext.Provider>
  );
}
