import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo rol" cross-componente. Mismo
 * patrón que <c>NuevaEmpresaContext</c> del módulo Administración: el
 * Provider vive a nivel ruta (<c>routes/_app/admin/roles/...</c>) y
 * cualquier botón "Nuevo" lo dispara con <c>useNuevoRol().abrir()</c>.
 */
export interface NuevoRolApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoRolContext = createContext<NuevoRolApi | null>(null);

export function useNuevoRol(): NuevoRolApi {
  const ctx = useContext(NuevoRolContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoRol() debe usarse dentro de <NuevoRolProvider>.',
    );
  }
  return ctx;
}
