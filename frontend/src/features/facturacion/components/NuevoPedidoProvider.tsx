import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { NuevoPedidoManual } from '@/features/facturacion/pages/NuevoPedidoManual';
import {
  NuevoPedidoContext,
  type NuevoPedidoApi,
} from '@/features/facturacion/components/nuevo-pedido-context';
import type { PedidoFacturableDetalleResponse } from '@/features/facturacion/api/types';

/**
 * <c>&lt;NuevoPedidoProvider/&gt;</c> + el Sheet asociado. Vive a nivel
 * shell (<c>routes/_app.tsx</c>) para que cualquier disparador del
 * módulo Facturación abra el mismo overlay. Espejo de
 * <c>NuevaRequisicionProvider</c> (patrón §6.2 de patrones-compras).
 *
 * <para>Confirm al cerrar con cambios: si el form reportó
 * <c>isDirty=true</c>, los gestos de cerrar muestran un
 * <c>window.confirm</c>. El flujo de éxito llama
 * <c>cerrar({ force: true })</c>.</para>
 */
export function NuevoPedidoProvider({ children }: { children: ReactNode }) {
  const [abierto, setAbierto] = useState(false);
  const [pedido, setPedido] = useState<
    PedidoFacturableDetalleResponse | undefined
  >(undefined);
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
      'Tienes cambios sin guardar en el nuevo pedido. ¿Descartarlos y cerrar?',
    );
    if (confirmar) {
      isDirtyRef.current = false;
      setAbierto(false);
    }
  }, []);

  const api = useMemo<NuevoPedidoApi>(
    () => ({
      abrir: (p?: PedidoFacturableDetalleResponse) => {
        isDirtyRef.current = false;
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
        <SheetContent
          side="right"
          className="w-full overflow-y-auto sm:max-w-3xl"
        >
          <SheetHeader>
            <SheetTitle>
              {esEdicion ? 'Editar pedido' : 'Nuevo pedido manual'}
            </SheetTitle>
            <SheetDescription>
              {esEdicion
                ? 'Modifica la cabecera y las líneas. La sucursal y el número de pedido son inmutables.'
                : 'Captura la cabecera y las líneas del pedido. Tras guardarlo queda listo para facturar desde la bandeja.'}
            </SheetDescription>
          </SheetHeader>

          <div className="px-2 pb-6">
            {abierto && (
              <NuevoPedidoManual
                onClose={cerrarConConfirm}
                onDirtyChange={setDirty}
                pedido={pedido}
              />
            )}
          </div>
        </SheetContent>
      </Sheet>
    </NuevoPedidoContext.Provider>
  );
}
