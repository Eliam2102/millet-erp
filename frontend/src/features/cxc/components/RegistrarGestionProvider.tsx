import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { RegistrarGestion } from '@/features/cxc/pages/RegistrarGestion';
import {
  RegistrarGestionContext,
  type RegistrarGestionApi,
} from '@/features/cxc/components/registrar-gestion-context';

/**
 * <c>&lt;RegistrarGestionProvider/&gt;</c> + Sheet "Registrar gestión de
 * cobranza" (CXC-FE-PR4). Vive a nivel shell. Espejo de
 * <c>NuevaLineaCreditoProvider</c>; <c>abrir()</c> acepta un
 * <c>clienteId</c> para preseleccionar al abrir desde la bitácora.
 */
export function RegistrarGestionProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const [clienteIdInicial, setClienteIdInicial] = useState<string | null>(null);
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<RegistrarGestionApi>(
    () => ({
      abrir: (opts) => {
        setDirty(false);
        setClienteIdInicial(opts?.clienteId ?? null);
        setAbierto(true);
      },
      cerrar: cerrarConConfirm,
      setDirty,
    }),
    [cerrarConConfirm, setDirty],
  );

  return (
    <RegistrarGestionContext.Provider value={api}>
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
            <SheetTitle>Registrar gestión de cobranza</SheetTitle>
            <SheetDescription>
              Deja el rastro del contacto con el cliente: canal, resultado y nota. Una promesa de
              pago exige monto y fecha comprometidos.
            </SheetDescription>
          </SheetHeader>

          <div className="flex min-h-0 flex-1 flex-col">
            {abierto && (
              <RegistrarGestion
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
                clienteIdInicial={clienteIdInicial}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
      {dialogo}
    </RegistrarGestionContext.Provider>
  );
}
