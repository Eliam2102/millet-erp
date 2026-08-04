import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nuevo artículo" cross-componente.
 * Mismo patrón que <c>NuevoProveedorContext</c>.
 */
export interface NuevoArticuloApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevoArticuloContext = createContext<NuevoArticuloApi | null>(
  null,
);

export function useNuevoArticulo(): NuevoArticuloApi {
  const ctx = useContext(NuevoArticuloContext);
  if (ctx == null) {
    throw new Error(
      'useNuevoArticulo() debe usarse dentro de <NuevoArticuloProvider>.',
    );
  }
  return ctx;
}
