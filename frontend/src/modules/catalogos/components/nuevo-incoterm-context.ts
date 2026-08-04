import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo Incoterm" cross-componente.
 */
export interface NuevoIncotermApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoIncotermContext = createContext<NuevoIncotermApi | null>(
  null,
);

export function useNuevoIncoterm(): NuevoIncotermApi {
  const ctx = useContext(NuevoIncotermContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoIncoterm() debe usarse dentro de <NuevoIncotermProvider>.',
    );
  }
  return ctx;
}
