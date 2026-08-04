import { createContext, useContext } from 'react';

/**
 * Context del Sheet "Nuevo anticipo" (FE-F4). Provider a nivel shell;
 * lo abren la bandeja de Control de Anticipos y el Quick Create. Mismo
 * patrón que <c>nuevo-pedido-context.ts</c>.
 */
export interface NuevoAnticipoApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoAnticipoContext = createContext<NuevoAnticipoApi | null>(null);

export function useNuevoAnticipo(): NuevoAnticipoApi {
  const ctx = useContext(NuevoAnticipoContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoAnticipo() debe usarse dentro de <NuevoAnticipoProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
