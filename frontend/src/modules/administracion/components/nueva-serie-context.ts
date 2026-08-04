import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nueva serie" cross-componente.
 * Mismo patrón que <c>NuevaEmpresaContext</c>: el Provider vive a
 * nivel ruta (<c>routes/_app/admin/series/index</c>) y cualquier
 * botón "Nueva serie" lo dispara con
 * <c>useNuevaSerie().abrir()</c>.
 */
export interface NuevaSerieApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaSerieContext = createContext<NuevaSerieApi | null>(null);

export function useNuevaSerie(): NuevaSerieApi {
  const ctx = useContext(NuevaSerieContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaSerie() debe usarse dentro de <NuevaSerieProvider>.',
    );
  }
  return ctx;
}
