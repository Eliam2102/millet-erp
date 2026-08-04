import { createContext, useContext } from 'react';

/**
 * Context para coordinar el Sheet "Nueva requisición" cross-componente.
 * El Provider vive a nivel <c>routes/_app.tsx</c> (shell) para que
 * cualquier botón ("Nueva" en bandeja, Quick Create del topbar,
 * "Nueva" en el master-detail) lo dispare con el mismo
 * <c>useNuevaRequisicion().abrir()</c>.
 *
 * <para>Vive aparte del Provider para satisfacer
 * <c>react-refresh/only-export-components</c> (componentes vs no-
 * componentes en archivos separados).</para>
 */
export interface NuevaRequisicionApi {
  /** Abre el Sheet. Si ya está abierto, no-op. */
  abrir: () => void;
  /** Cierra el Sheet. Llamado por el wrapper al success o cancel. Si
   * el form tiene cambios sin guardar y el caller no pide forzar,
   * el provider muestra un confirm antes de cerrar (devuelve sin
   * cerrar si el usuario cancela). Pasar <c>force</c> en flujos de
   * éxito (post-submit) o cuando ya se confirmó externamente. */
  cerrar: (opts?: { force?: boolean }) => void;
  /** Reporta al provider si el form interno tiene cambios sin
   * guardar. El form lo llama cuando su <c>isDirty</c> cambia
   * (efecto). Default tracked: <c>false</c> al montar. */
  setDirty: (dirty: boolean) => void;
}

export const NuevaRequisicionContext =
  createContext<NuevaRequisicionApi | null>(null);

/**
 * Lee el API del Sheet desde el contexto. Lanza si no hay provider —
 * preferimos crash explícito en dev a un silent no-op.
 */
export function useNuevaRequisicion(): NuevaRequisicionApi {
  const ctx = useContext(NuevaRequisicionContext);
  if (ctx == null) {
    throw new Error(
      'useNuevaRequisicion() debe usarse dentro de <NuevaRequisicionProvider>. Probable causa: la ruta no está bajo `_app`.',
    );
  }
  return ctx;
}
