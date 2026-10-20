import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
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
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevaLineaCreditoApi>(
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
        <SheetContent side="right" size="lg">
          <SheetHeader>
            <SheetTitle>Nueva línea de crédito</SheetTitle>
            <SheetDescription>
              Define límite, moneda, origen (SOLUNION / interno) y plazo del cliente. Solo puede
              existir una línea activa por cliente y moneda.
            </SheetDescription>
          </SheetHeader>

          <div className="flex min-h-0 flex-1 flex-col">
            {abierto && <NuevaLineaCredito onClose={cerrarConConfirm} onDirtyChange={setDirty} />}
          </div>
        </SheetContent>
      </Sheet>
      {dialogo}
    </NuevaLineaCreditoContext.Provider>
  );
}
