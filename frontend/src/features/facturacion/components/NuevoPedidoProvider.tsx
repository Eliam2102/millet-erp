import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import type { PedidoFacturableDetalleResponse } from '@/features/facturacion/api/types';
import {
  NuevoPedidoContext,
  type NuevoPedidoApi,
} from '@/features/facturacion/components/nuevo-pedido-context';
import { NuevoPedidoManual } from '@/features/facturacion/pages/NuevoPedidoManual';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';

/**
 * <c>&lt;NuevoPedidoProvider/&gt;</c> + el Sheet asociado. Vive a nivel
 * shell (<c>routes/_app.tsx</c>) para que cualquier disparador del
 * módulo Facturación abra el mismo overlay. Espejo de
 * <c>NuevaRequisicionProvider</c> (patrón §6.2 de patrones-compras).
 *
 * <para>Confirm al cerrar con cambios: si el form reportó
 * <c>isDirty=true</c>, los gestos de cerrar muestran el
 * diálogo de <c>useConfirmarDescarte</c>. El flujo de éxito llama
 * <c>cerrar({ force: true })</c>.</para>
 */
export function NuevoPedidoProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const [pedido, setPedido] = useState<PedidoFacturableDetalleResponse | undefined>(undefined);
  const alCerrar = useCallback(() => setAbierto(false), []);
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(alCerrar);
  const cerrarConConfirm = useCallback(
    (opts?: { force?: boolean }) => solicitarCierre(opts?.force),
    [solicitarCierre],
  );

  const api = useMemo<NuevoPedidoApi>(
    () => ({
      abrir: (p?: PedidoFacturableDetalleResponse) => {
        setDirty(false);
        setPedido(p);
        setAbierto(true);
      },
      cerrar: cerrarConConfirm,
      setDirty,
    }),
    [cerrarConConfirm, setDirty],
  );

  const esEdicion = pedido != null;

  return (
    <NuevoPedidoContext.Provider value={api}>
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
            <SheetTitle>{esEdicion ? 'Editar pedido' : 'Nuevo pedido manual'}</SheetTitle>
            <SheetDescription>
              {esEdicion
                ? 'Modifica la cabecera y las partidas. La sucursal y el número de pedido son inmutables.'
                : 'Captura la cabecera y las partidas del pedido. Tras guardarlo queda listo para facturar desde la bandeja.'}
            </SheetDescription>
          </SheetHeader>
          <SheetBody className="flex flex-col overflow-hidden py-0">
            <div className="contents">
              {abierto && (
                <NuevoPedidoManual
                  onClose={cerrarConConfirm}
                  onDirtyChange={setDirty}
                  pedido={pedido}
                />
              )}
            </div>
          </SheetBody>
        </SheetContent>
      </Sheet>
      {dialogo}
    </NuevoPedidoContext.Provider>
  );
}
