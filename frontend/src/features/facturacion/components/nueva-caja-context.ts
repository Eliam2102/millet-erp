import { createContext, useContext } from 'react';

/**
 * Context del Sheet "Nueva caja" (CAJAS-PR5). Provider a nivel shell;
 * lo abre la bandeja de cajas. Espejo de <c>nuevo-repp-context</c>.
 */
export interface NuevaCajaApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaCajaContext = createContext<NuevaCajaApi | null>(null);

export function useNuevaCaja(): NuevaCajaApi {
  const ctx = useContext(NuevaCajaContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaCaja() debe usarse dentro de <NuevaCajaProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
