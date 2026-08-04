import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo producto A+W"
 * cross-componente. Mismo patrón que <c>NuevoClienteContext</c>: el
 * Provider vive a nivel ruta
 * (<c>routes/_app/admin/datos-maestros/productos-aw/...</c>) y
 * cualquier botón "Nuevo" lo dispara con
 * <c>useNuevoProductoAw().abrir()</c>.
 */
export interface NuevoProductoAwApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoProductoAwContext =
  createContext<NuevoProductoAwApi | null>(null);

export function useNuevoProductoAw(): NuevoProductoAwApi {
  const ctx = useContext(NuevoProductoAwContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoProductoAw() debe usarse dentro de <NuevoProductoAwProvider>.',
    );
  }
  return ctx;
}
