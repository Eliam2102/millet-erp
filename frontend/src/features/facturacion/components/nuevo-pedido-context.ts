import { createContext, useContext } from 'react';
import type { PedidoFacturableDetalleResponse } from '@/features/facturacion/api/types';

/**
 * Context para coordinar el Sheet "Nuevo pedido manual" cross-componente.
 * El Provider vive a nivel <c>routes/_app.tsx</c> (shell) para que
 * cualquier disparador (botón "Nuevo pedido" de la bandeja, Quick
 * Create del topbar) lo abra con el mismo <c>useNuevoPedido().abrir()</c>.
 *
 * <para>Vive aparte del Provider para satisfacer
 * <c>react-refresh/only-export-components</c>. Mismo patrón que
 * <c>nueva-requisicion-context.ts</c> de Compras.</para>
 */
export interface NuevoPedidoApi {
  /** Abre el Sheet. Con `pedido`, entra en modo EDICIÓN (PUT con ETag);
   * sin él, captura un pedido nuevo. */
  abrir: (pedido?: PedidoFacturableDetalleResponse) => void;
  /** Cierra el Sheet. Si el form tiene cambios sin guardar y no se
   * fuerza, el provider confirma antes de cerrar. */
  cerrar: (opts?: { force?: boolean }) => void;
  /** Reporta al provider si el form interno está dirty. */
  setDirty: (dirty: boolean) => void;
}

export const NuevoPedidoContext = createContext<NuevoPedidoApi | null>(null);

export function useNuevoPedido(): NuevoPedidoApi {
  const ctx = useContext(NuevoPedidoContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoPedido() debe usarse dentro de <NuevoPedidoProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
