import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo usuario" cross-componente.
 * Mismo patrón que <c>NuevoRolContext</c> / <c>NuevaEmpresaContext</c>:
 * el Provider vive a nivel ruta (<c>routes/_app/admin/usuarios/...</c>)
 * y cualquier botón "Nuevo" lo dispara con
 * <c>useNuevoUsuario().abrir()</c>.
 */
export interface NuevoUsuarioApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoUsuarioContext = createContext<NuevoUsuarioApi | null>(null);

export function useNuevoUsuario(): NuevoUsuarioApi {
  const ctx = useContext(NuevoUsuarioContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoUsuario() debe usarse dentro de <NuevoUsuarioProvider>.',
    );
  }
  return ctx;
}
