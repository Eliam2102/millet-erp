import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo cliente" cross-componente.
 * Mismo patrón que <c>NuevoProveedorContext</c>: el Provider vive a
 * nivel ruta (<c>routes/_app/admin/datos-maestros/clientes/...</c>) y
 * cualquier botón "Nuevo" lo dispara con
 * <c>useNuevoCliente().abrir()</c>.
 */
export interface NuevoClienteApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoClienteContext = createContext<NuevoClienteApi | null>(null);

export function useNuevoCliente(): NuevoClienteApi {
  const ctx = useContext(NuevoClienteContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoCliente() debe usarse dentro de <NuevoClienteProvider>.',
    );
  }
  return ctx;
}
