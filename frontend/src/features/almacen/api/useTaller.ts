import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * PT5.10 — Taller de sucursal: lo descargado por pedidos internos Serie 9000, el cierre de día del almacenista y su
 * factor de optimización. Mirror de <c>TallerEndpoints</c>.
 */
export interface DescargaTaller {
  numeroPedido: string;
  posicion: number;
  articuloRef: string;
  cantidad: number;
  anchoMm: number | null;
  altoMm: number | null;
  m2: number | null;
  estado: 'RECIBIDO' | 'EN_REVISION';
  folio: string | null;
  error: string | null;
}

export interface CierreTaller {
  id: string;
  sucursalId: string;
  fecha: string;
  hojasTomadas: number;
  retazosTomados: number;
  m2Descargados: number;
  m2Cortados: number;
  pedidosServidos: number;
  pedidosSerie9000: number;
  cortadores: number;
  factorOptimizacion: number | null;
  formula: string;
  notas: string | null;
  cerradoAt: string;
  corregidoAt: string | null;
}

export interface PrellenadoCierreTaller {
  sucursalId: string;
  fecha: string;
  hojasDescargadas: number;
  m2Descargados: number;
  pedidosSerie9000: number;
  pedidosServidos: number;
  enRevision: number;
  descargas: DescargaTaller[];
  cierre: CierreTaller | null;
}

export interface CapturaCierreTaller {
  hojasTomadas: number;
  retazosTomados: number;
  m2Cortados: number;
  pedidosServidos: number;
  cortadores: number;
  notas: string | null;
}

const tallerKeys = {
  prellenado: (sucursalId: string, fecha: string) =>
    ['almacen', 'taller', 'prellenado', sucursalId, fecha] as const,
};

export function usePrellenadoCierreTaller(sucursalId: string | null, fecha: string) {
  return useQuery({
    queryKey: tallerKeys.prellenado(sucursalId ?? '', fecha),
    enabled: !!sucursalId && !!fecha,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<PrellenadoCierreTaller>(
        `/api/v1/almacen/taller/cierres/prellenado?sucursalId=${sucursalId}&fecha=${fecha}`,
        { signal },
      );
      return data;
    },
  });
}

/** Cierra el día (POST) o, con <c>cierreId</c>, lo corrige (PUT; solo quien administra almacenes). */
export function useGuardarCierreTaller() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      sucursalId: string;
      fecha: string;
      cierreId?: string;
      captura: CapturaCierreTaller;
    }) => {
      const { data } = args.cierreId
        ? await apiRequest<CierreTaller>(`/api/v1/almacen/taller/cierres/${args.cierreId}`, {
            method: 'PUT',
            body: args.captura,
          })
        : await apiRequest<CierreTaller>('/api/v1/almacen/taller/cierres', {
            method: 'POST',
            body: { sucursalId: args.sucursalId, fecha: args.fecha, ...args.captura },
          });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['almacen', 'taller'] }),
  });
}
