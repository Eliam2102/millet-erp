import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
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
import type { DepositoPorIdentificarItem } from '@/features/cxc/api/types';

/**
 * <c>&lt;NuevaPropuestaProvider/&gt;</c> + Sheet "Nueva propuesta de
 * aplicación" (CXC-FE-PR6). Vive a nivel shell. Espejo de
 * <c>RegistrarGestionProvider</c>.
 */
export function NuevaPropuestaProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const [deposito, setDeposito] = useState<DepositoPorIdentificarItem | null>(null);
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevaPropuestaApi>(
    () => ({
      abrir: (dep?: DepositoPorIdentificarItem) => {
        setDirty(false);
        setDeposito(dep ?? null);
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
        <SheetContent side="right" size="lg">
          <SheetHeader>
            <SheetTitle>
              {deposito ? 'Identificar depósito' : 'Nueva propuesta de aplicación'}
            </SheetTitle>
            <SheetDescription>
              Matching depósito ↔ facturas desde el remittance del cliente. CxC propone; Ingresos
              confirma o rechaza.
            </SheetDescription>
          </SheetHeader>

          <div className="flex min-h-0 flex-1 flex-col">
            {abierto && (
              <NuevaPropuestaAplicacion
                key={deposito?.movimientoBancarioId ?? 'nueva'}
                deposito={deposito}
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
      {dialogo}
    </NuevaPropuestaContext.Provider>
  );
}
