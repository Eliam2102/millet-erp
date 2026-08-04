import { createContext, useContext } from 'react';

/**
 * Context del Sheet "Nueva propuesta de aplicación" (CXC-FE-PR6).
 * Provider a nivel shell; lo abren la bandeja de aplicaciones y el
 * Quick Create. Mismo patrón que <c>registrar-gestion-context.ts</c>.
 */
export interface NuevaPropuestaApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaPropuestaContext = createContext<NuevaPropuestaApi | null>(null);

export function useNuevaPropuesta(): NuevaPropuestaApi {
  const ctx = useContext(NuevaPropuestaContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaPropuesta() debe usarse dentro de <NuevaPropuestaProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
