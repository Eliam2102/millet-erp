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
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';

/**
 * <c>&lt;NuevaCartaPorteProvider/&gt;</c> + Sheet de captura (FE-F8). Vive a
 * nivel shell. Espejo de <c>NuevoReppProvider</c>.
 */
export function NuevaCartaPorteProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevaCartaPorteApi>(
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
      {dialogo}
    </NuevaCartaPorteContext.Provider>
  );
}
