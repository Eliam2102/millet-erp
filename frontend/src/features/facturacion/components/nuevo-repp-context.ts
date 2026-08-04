import { createContext, useContext } from 'react';

/**
 * Context del Sheet "Nuevo REPP" (complemento de pago, FE-F6). Provider a
 * nivel shell; lo abren la bandeja de REPP y el Quick Create.
 */
export interface NuevoReppApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoReppContext = createContext<NuevoReppApi | null>(null);

export function useNuevoRepp(): NuevoReppApi {
  const ctx = useContext(NuevoReppContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoRepp() debe usarse dentro de <NuevoReppProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
