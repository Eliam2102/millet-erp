import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
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
import { useCallback, useMemo, useState, type ReactNode } from 'react';

/**
 * <c>&lt;NuevaCajaProvider/&gt;</c> + Sheet de alta de caja (CAJAS-PR5).
 * Vive a nivel shell. Espejo de <c>NuevoReppProvider</c>.
 */
export function NuevaCajaProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevaCajaApi>(
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
              La caja nace activa y sin alcance (todas las sucursales y canales); asigna sucursales,
              canales y cajeros desde su detalle.
            </SheetDescription>
          </SheetHeader>
          <SheetBody>
            <div className="min-w-0">
              {abierto && <NuevaCaja onClose={cerrarConConfirm} onDirtyChange={setDirty} />}
            </div>
          </SheetBody>
        </SheetContent>
      </Sheet>
      {dialogo}
    </NuevaCajaContext.Provider>
  );
}
