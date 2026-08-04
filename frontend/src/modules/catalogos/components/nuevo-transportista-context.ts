import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo transportista"
 * cross-componente.
 */
export interface NuevoTransportistaApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoTransportistaContext =
  createContext<NuevoTransportistaApi | null>(null);

export function useNuevoTransportista(): NuevoTransportistaApi {
  const ctx = useContext(NuevoTransportistaContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoTransportista() debe usarse dentro de <NuevoTransportistaProvider>.',
    );
  }
  return ctx;
}
