import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nueva empresa" cross-componente.
 * Mismo patrón que <c>NuevaRequisicionContext</c> del módulo Compras:
 * el Provider vive a nivel ruta (<c>routes/_app/admin/empresas/...</c>)
 * y cualquier botón "Nueva" lo dispara con
 * <c>useNuevaEmpresa().abrir()</c>.
 */
export interface NuevaEmpresaApi {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (dirty: boolean) => void;
}

export const NuevaEmpresaContext = createContext<NuevaEmpresaApi | null>(null);

export function useNuevaEmpresa(): NuevaEmpresaApi {
  const ctx = useContext(NuevaEmpresaContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaEmpresa() debe usarse dentro de <NuevaEmpresaProvider>.',
    );
  }
  return ctx;
}
