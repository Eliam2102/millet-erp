import { createContext, useContext } from 'react';

/**
 * Context del Sheet "Nueva Carta Porte" (FE-F8). Provider a nivel shell;
 * lo abren la bandeja de Carta Porte y el Quick Create.
 */
export interface NuevaCartaPorteApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaCartaPorteContext =
  createContext<NuevaCartaPorteApi | null>(null);

export function useNuevaCartaPorte(): NuevaCartaPorteApi {
  const ctx = useContext(NuevaCartaPorteContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaCartaPorte() debe usarse dentro de <NuevaCartaPorteProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
