import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo uso principal"
 * cross-componente.
 */
export interface NuevoUsoPrincipalApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoUsoPrincipalContext =
  createContext<NuevoUsoPrincipalApi | null>(null);

export function useNuevoUsoPrincipal(): NuevoUsoPrincipalApi {
  const ctx = useContext(NuevoUsoPrincipalContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoUsoPrincipal() debe usarse dentro de <NuevoUsoPrincipalProvider>.',
    );
  }
  return ctx;
}
