import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nueva moneda" cross-componente.
 * Mismo patrón que <c>NuevoArticuloContext</c>.
 */
export interface NuevaMonedaApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaMonedaContext = createContext<NuevaMonedaApi | null>(null);

export function useNuevaMoneda(): NuevaMonedaApi {
  const ctx = useContext(NuevaMonedaContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaMoneda() debe usarse dentro de <NuevaMonedaProvider>.',
    );
  }
  return ctx;
}
