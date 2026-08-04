import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nueva orden de compra" cross-
 * componente. Mismo patrón que <c>NuevaRequisicionContext</c> de RQ:
 * el Provider vive a nivel <c>routes/_app.tsx</c> (shell) para que
 * cualquier botón ("Nueva OC" en bandeja, Quick Create del topbar,
 * "Convertir" desde RQ futuro) lo dispare con el mismo
 * <c>useNuevaOrdenCompra().abrir()</c>.
 *
 * <para>Vive aparte del Provider para satisfacer
 * <c>react-refresh/only-export-components</c>.</para>
 */
/**
 * Datos de la RQ origen cuando el Sheet se abre en modo "Convertir 1:1"
 * desde el detalle de una RQ. El Sheet usa <c>sucursalId</c> para
 * pre-llenar el campo de sucursal destino, y luego pre-selecciona la
 * RQ una vez que <c>useRequisicionesDisponibles</c> la lista (filtrada
 * por sucursal coincidente).
 */
export interface DesdeRequisicionContext {
  requisicionId: string;
  sucursalId: string;
  /** Folio mostrado en el header del Sheet ("Convirtiendo MID2026-..."). */
  folio?: string;
}

export interface NuevaOrdenCompraApi {
  /**
   * Abre el Sheet. Si ya está abierto, no-op. Cuando se pasa
   * <c>desdeRequisicion</c>, el Sheet entra en modo 1:1: pre-llena
   * sucursal, pre-selecciona la RQ, oculta el botón "Agregar
   * requisiciones (consolidación)" (no se mezclan modos).
   */
  abrir: (opts?: { desdeRequisicion?: DesdeRequisicionContext }) => void;
  /** Cierra el Sheet. <c>force=true</c> en flujos de éxito (post-submit)
   * para saltar el confirm de "tienes cambios sin guardar". */
  cerrar: (opts?: { force?: boolean }) => void;
  /** Reporta al provider si el form interno tiene cambios sin
   * guardar. El form lo llama desde un useEffect cuando isDirty
   * cambia. */
  setDirty: (dirty: boolean) => void;
}

export const NuevaOrdenCompraContext =
  createContext<NuevaOrdenCompraApi | null>(null);

export function useNuevaOrdenCompra(): NuevaOrdenCompraApi {
  const ctx = useContext(NuevaOrdenCompraContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaOrdenCompra() debe usarse dentro de <NuevaOrdenCompraProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
