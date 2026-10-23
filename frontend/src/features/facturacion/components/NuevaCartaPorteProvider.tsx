import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  NuevaCartaPorteContext,
  type NuevaCartaPorteApi,
} from '@/features/facturacion/components/nueva-carta-porte-context';
import { NuevaCartaPorte } from '@/features/facturacion/pages/NuevaCartaPorte';
import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';

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
    const confirmar = window.confirm('Tienes una Carta Porte sin emitir. ¿Descartar y cerrar?');
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
        <SheetContent side="right" size="lg">
          <SheetHeader>
            <SheetTitle>Nueva Carta Porte (3.1)</SheetTitle>
            <SheetDescription>
              CFDI de Traslado (T) o Ingreso (I). Captura el tramo, el vehículo, el operador y las
              mercancías.
            </SheetDescription>
          </SheetHeader>
          <SheetBody className="flex flex-col overflow-hidden py-0">
            <div className="contents">
              {abierto && <NuevaCartaPorte onClose={cerrarConConfirm} onDirtyChange={setDirty} />}
            </div>
          </SheetBody>
        </SheetContent>
      </Sheet>
    </NuevaCartaPorteContext.Provider>
  );
}
