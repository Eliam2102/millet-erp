import { createContext, useContext } from 'react';

/**
 * Context del Sheet "Nueva Carta Porte" (FE-F8). Provider a nivel shell;
 * lo abren la bandeja de Carta Porte y el Quick Create.
 */
/** M-354: datos del tramo de vehículo propio con los que se prellena la emisión (y que la ligan al tramo). */
export interface TramoPrefill {
  tramoId: string;
  pedidoFacturableId: string;
  origen: string;
  destino: string;
  origenCodigoPostal: string | null;
  origenEstado: string | null;
  destinoCodigoPostal: string | null;
  destinoEstado: string | null;
  distanciaKm: number | null;
}

export interface NuevaCartaPorteApi {
  abrir: (tramo?: TramoPrefill) => void;
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
