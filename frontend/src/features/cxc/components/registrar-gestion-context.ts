import { createContext, useContext } from 'react';

/**
 * Context del Sheet "Registrar gestión de cobranza" (CXC-FE-PR4).
 * Provider a nivel shell; lo abren la pantalla de cobranza (con el
 * cliente preseleccionado) y el Quick Create (sin preselección). Mismo
 * patrón que <c>nueva-linea-credito-context.ts</c>.
 */
export interface RegistrarGestionApi {
  abrir: (opts?: { clienteId?: string }) => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const RegistrarGestionContext =
  createContext<RegistrarGestionApi | null>(null);

export function useRegistrarGestion(): RegistrarGestionApi {
  const ctx = useContext(RegistrarGestionContext);
  if (ctx == null) {
    throw new Error(
      'useRegistrarGestion() debe usarse dentro de <RegistrarGestionProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
