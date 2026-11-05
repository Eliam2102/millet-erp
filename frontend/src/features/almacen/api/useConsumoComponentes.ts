import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/** A4.8: norma de consumo de un componente (espejo de `NormaConsumoResponse`). */
export interface NormaConsumo {
  articuloId: string;
  articuloClave: string | null;
  articuloDescripcion: string | null;
  unidadMedida: string | null;
  consumoPorUnidad: number;
  porcentajeMerma: number;
  factorM3PorM2: number | null;
  updatedAt: string;
}

export type CapturaNormaConsumo = Pick<NormaConsumo, 'consumoPorUnidad' | 'porcentajeMerma' | 'factorM3PorM2'>;

/** Fila de la bandeja de revisión (`FilaBandeja`); los consumos de componentes traen `tipo = CONSUMO_COMPONENTE`. */
export interface ConsumoEnRevision {
  mensajeId: string;
  tipo: string;
  estado: 'EN_REVISION' | 'EXCEPCION';
  referencia: string | null;
  error: string | null;
  recibidoAt: string;
  payload: string;
}

export interface ConsumoPayload {
  etiqueta: string;
  operacion: number;
  componente: string;
  capas: number;
  anchoMm: number | null;
  altoMm: number | null;
  sucursal: string | null;
}

export function useNormasConsumo() {
  return useQuery({
    queryKey: almacenKeys.normasConsumo(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<NormaConsumo[]>('/api/v1/almacen/normas-consumo', { signal });
      return data;
    },
  });
}

export function useGuardarNormaConsumo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { articuloId: string; captura: CapturaNormaConsumo }) => {
      const { data } = await apiRequest<NormaConsumo>(`/api/v1/almacen/normas-consumo/${args.articuloId}`, {
        method: 'PUT',
        body: args.captura,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}

/** Consumos de componentes sin descontar; la bandeja del backend los mezcla con las salidas de la app de Almacenes. */
export function useConsumosEnRevision() {
  return useQuery({
    queryKey: almacenKeys.consumosRevision(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ConsumoEnRevision[]>('/api/v1/almacen/mensajes-app/revision', { signal });
      return data.filter((f) => f.tipo === 'CONSUMO_COMPONENTE');
    },
  });
}
