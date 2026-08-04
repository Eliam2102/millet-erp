import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo proveedor" cross-componente.
 * Mismo patrón que <c>NuevaEmpresaContext</c>: el Provider vive a nivel
 * ruta (<c>routes/_app/admin/datos-maestros/proveedores/...</c>) y
 * cualquier botón "Nuevo" lo dispara con
 * <c>useNuevoProveedor().abrir()</c>.
 */
export interface NuevoProveedorApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoProveedorContext =
  createContext<NuevoProveedorApi | null>(null);

export function useNuevoProveedor(): NuevoProveedorApi {
  const ctx = useContext(NuevoProveedorContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoProveedor() debe usarse dentro de <NuevoProveedorProvider>.',
    );
  }
  return ctx;
}
