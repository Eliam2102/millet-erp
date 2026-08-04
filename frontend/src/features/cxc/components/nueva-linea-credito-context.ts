import { createContext, useContext } from 'react';

/**
 * Context del Sheet "Nueva línea de crédito" (CXC-FE-PR2). Provider a
 * nivel shell; lo abren la bandeja de líneas y el Quick Create. Mismo
 * patrón que <c>nuevo-anticipo-context.ts</c>.
 */
export interface NuevaLineaCreditoApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaLineaCreditoContext =
  createContext<NuevaLineaCreditoApi | null>(null);

export function useNuevaLineaCredito(): NuevaLineaCreditoApi {
  const ctx = useContext(NuevaLineaCreditoContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaLineaCredito() debe usarse dentro de <NuevaLineaCreditoProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
