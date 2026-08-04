import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nueva condición de pago"
 * cross-componente. Mismo patrón que <c>NuevoArticuloContext</c>.
 */
export interface NuevaCondicionesPagoApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaCondicionesPagoContext =
  createContext<NuevaCondicionesPagoApi | null>(null);

export function useNuevaCondicionesPago(): NuevaCondicionesPagoApi {
  const ctx = useContext(NuevaCondicionesPagoContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaCondicionesPago() debe usarse dentro de <NuevaCondicionesPagoProvider>.',
    );
  }
  return ctx;
}
