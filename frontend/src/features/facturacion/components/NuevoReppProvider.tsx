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
  NuevoReppContext,
  type NuevoReppApi,
} from '@/features/facturacion/components/nuevo-repp-context';
import { NuevoRepp } from '@/features/facturacion/pages/NuevoRepp';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';

/**
 * <c>&lt;NuevoReppProvider/&gt;</c> + Sheet de emisión de REPP (FE-F6).
 * Vive a nivel shell. Espejo de <c>NuevoAnticipoProvider</c>.
 */
export function NuevoReppProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevoReppApi>(
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
        <SheetContent side="right" size="lg">
          <SheetHeader>
            <SheetTitle>Nuevo complemento de pago (REPP)</SheetTitle>
            <ContextoSucursal />
            <SheetDescription>
              Pago 2.0 multi-factura. Captura el pago y las facturas que cubre; todas deben ser del
              mismo receptor.
            </SheetDescription>
          </SheetHeader>
          <SheetBody className="flex flex-col overflow-hidden py-0">
            <div className="contents">
              {abierto && <NuevoRepp onClose={cerrarConConfirm} onDirtyChange={setDirty} />}
            </div>
          </SheetBody>
        </SheetContent>
      </Sheet>
      {dialogo}
    </NuevoReppContext.Provider>
  );
}
