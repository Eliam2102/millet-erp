import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nueva unidad de medida"
 * cross-componente (ADR-0046).
 */
export interface NuevaUnidadMedidaApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaUnidadMedidaContext =
  createContext<NuevaUnidadMedidaApi | null>(null);

export function useNuevaUnidadMedida(): NuevaUnidadMedidaApi {
  const ctx = useContext(NuevaUnidadMedidaContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaUnidadMedida() debe usarse dentro de <NuevaUnidadMedidaProvider>.',
    );
  }
  return ctx;
}
