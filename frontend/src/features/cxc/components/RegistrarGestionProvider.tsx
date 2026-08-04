import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
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
      'Tienes una gestión sin registrar. ¿Descartar y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<RegistrarGestionApi>(
    () => ({
      abrir: (opts) => {
        isDirtyRef.current = false;
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
        <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-2xl">
          <SheetHeader>
            <SheetTitle>Registrar gestión de cobranza</SheetTitle>
            <SheetDescription>
              Deja el rastro del contacto con el cliente: canal, resultado
              y nota. Una promesa de pago exige monto y fecha comprometidos.
            </SheetDescription>
          </SheetHeader>

          <div className="px-2 pb-6">
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
    </RegistrarGestionContext.Provider>
  );
}
