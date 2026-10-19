import { create } from 'zustand';

/**
 * Filtros de la bandeja de clientes. Viven fuera del componente porque las
 * rutas <c>clientes/index.tsx</c> y <c>clientes/$id.tsx</c> montan cada una
 * su propio <c>ClientesLayout</c>; con <c>useState</c> los filtros se
 * reiniciaban al abrir un cliente (M-23).
 *
 * <para>Sin persistencia a disco: memoria pura, mismo criterio que
 * <c>auth-store</c>. Se reinicia al recargar el navegador.</para>
 */
export interface ClientesFiltrosState {
  sinClasificar: boolean;
  rfcInput: string;
  razonInput: string;
  origen: string;
  estatus: string;
  soloFiscalesIncompletos: boolean;
  resultadoSync: string;
}

interface ClientesFiltrosActions {
  setSinClasificar: (v: boolean) => void;
  setRfcInput: (v: string) => void;
  setRazonInput: (v: string) => void;
  setOrigen: (v: string) => void;
  setEstatus: (v: string) => void;
  setSoloFiscalesIncompletos: (v: boolean) => void;
  setResultadoSync: (v: string) => void;
}

const initialState: ClientesFiltrosState = {
  sinClasificar: false,
  rfcInput: '',
  razonInput: '',
  origen: '',
  estatus: '',
  soloFiscalesIncompletos: false,
  resultadoSync: '',
};

export const useClientesFiltrosStore = create<
  ClientesFiltrosState & ClientesFiltrosActions
>((set) => ({
  ...initialState,

  setSinClasificar: (sinClasificar) => set({ sinClasificar }),
  setRfcInput: (rfcInput) => set({ rfcInput }),
  setRazonInput: (razonInput) => set({ razonInput }),
  setOrigen: (origen) => set({ origen }),
  setEstatus: (estatus) => set({ estatus }),
  setSoloFiscalesIncompletos: (soloFiscalesIncompletos) =>
    set({ soloFiscalesIncompletos }),
  setResultadoSync: (resultadoSync) => set({ resultadoSync }),
}));

/** Vuelve los filtros a su valor inicial (uso: tests y cierre de sesión). */
export function resetClientesFiltros(): void {
  useClientesFiltrosStore.setState(initialState);
}
