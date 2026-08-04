import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nueva categoría de artículo"
 * cross-componente (mismo patrón que Usos Principales).
 */
export interface NuevaCategoriaArticuloApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaCategoriaArticuloContext =
  createContext<NuevaCategoriaArticuloApi | null>(null);

export function useNuevaCategoriaArticulo(): NuevaCategoriaArticuloApi {
  const ctx = useContext(NuevaCategoriaArticuloContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaCategoriaArticulo() debe usarse dentro de <NuevaCategoriaArticuloProvider>.',
    );
  }
  return ctx;
}
