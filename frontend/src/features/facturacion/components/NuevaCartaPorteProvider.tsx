import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { NuevaCartaPorte } from '@/features/facturacion/pages/NuevaCartaPorte';
import {
  NuevaCartaPorteContext,
  type NuevaCartaPorteApi,
} from '@/features/facturacion/components/nueva-carta-porte-context';

/**
 * <c>&lt;NuevaCartaPorteProvider/&gt;</c> + Sheet de captura (FE-F8). Vive a
 * nivel shell. Espejo de <c>NuevoReppProvider</c>.
 */
export function NuevaCartaPorteProvider({ children }: { children: ReactNode }) {
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
      'Tienes una Carta Porte sin emitir. ¿Descartar y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevaCartaPorteApi>(
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
    <NuevaCartaPorteContext.Provider value={api}>
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
            <SheetTitle>Nueva Carta Porte (3.1)</SheetTitle>
            <SheetDescription>
              CFDI de Traslado (T) o Ingreso (I). Captura el tramo, el vehículo,
              el operador y las mercancías.
            </SheetDescription>
          </SheetHeader>

          <div className="px-2 pb-6">
            {abierto && (
              <NuevaCartaPorte onClose={cerrarConConfirm} onDirtyChange={setDirty} />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevaCartaPorteContext.Provider>
  );
}
