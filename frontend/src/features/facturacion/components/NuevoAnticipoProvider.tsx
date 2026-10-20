import { ContextoSucursal } from '@/components/erp/display/ContextoSucursal';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import {
  NuevoAnticipoContext,
  type NuevoAnticipoApi,
} from '@/features/facturacion/components/nuevo-anticipo-context';
import { NuevoAnticipo } from '@/features/facturacion/pages/NuevoAnticipo';
import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';

/**
 * <c>&lt;NuevoAnticipoProvider/&gt;</c> + Sheet de emisión de anticipo
 * (FE-F4). Vive a nivel shell. Espejo de <c>NuevoPedidoProvider</c>.
 */
export function NuevoAnticipoProvider({ children }: { children: ReactNode }) {
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
    const confirmar = window.confirm('Tienes un anticipo sin emitir. ¿Descartar y cerrar?');
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoAnticipoApi>(
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
    <NuevoAnticipoContext.Provider value={api}>
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
            <SheetTitle>Nuevo anticipo</SheetTitle>
            <ContextoSucursal />
            <SheetDescription>
              Emite una factura de anticipo. Es nominal: requiere el RFC y datos fiscales del
              cliente. Se amortiza en la factura final (relación 07).
            </SheetDescription>
          </SheetHeader>
          <SheetBody className="flex flex-col overflow-hidden py-0">
            <div className="contents">
              {abierto && <NuevoAnticipo onClose={cerrarConConfirm} onDirtyChange={setDirty} />}
            </div>
          </SheetBody>
        </SheetContent>
      </Sheet>
    </NuevoAnticipoContext.Provider>
  );
}
