import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/** ALM-03: salida por rotura en almacén (espejo de `SalidaRoturaResponse`). */
export interface SalidaRotura {
  id: string;
  sucursalId: string;
  ubicacionId: string;
  ubicacionClave: string | null;
  articuloId: string;
  articuloClave: string | null;
  articuloDescripcion: string | null;
  cantidad: number;
  unidadMedida: string;
  fecha: string;
  motivoCodigo: string;
  motivo: string;
  comentario: string | null;
  estado: 'PENDIENTE' | 'AUTORIZADA' | 'RECHAZADA';
  capturadoPor: string;
  capturadoAt: string;
  decididoPor: string | null;
  decididoAt: string | null;
  motivoRechazo: string | null;
  movimientoId: string | null;
  folioMovimiento: string | null;
  costoUnitarioMxn: number | null;
  valorMxn: number | null;
}

export interface MotivoRotura {
  codigo: string;
  texto: string;
}

export interface CapturarRoturaPayload {
  ubicacionId: string;
  articuloId: string;
  cantidad: number;
  fecha: string;
  motivoCodigo: string;
  comentario: string | null;
}

export function useSalidasRotura() {
  return useQuery({
    queryKey: almacenKeys.salidasRotura({}),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<SalidaRotura[]>('/api/v1/almacen/salidas-rotura', { signal });
      return data;
    },
  });
}

export function useMotivosRotura() {
  return useQuery({
    queryKey: almacenKeys.motivosRotura(),
    staleTime: Infinity,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<MotivoRotura[]>('/api/v1/almacen/salidas-rotura/motivos', { signal });
      return data;
    },
  });
}

export function useCapturarRotura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { command: CapturarRoturaPayload; idempotencyKey: string }) => {
      const { data } = await apiRequest<SalidaRotura>('/api/v1/almacen/salidas-rotura', {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}

export function useDecidirRotura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; accion: 'autorizar' | 'rechazar'; motivo?: string }) => {
      const { data } = await apiRequest<SalidaRotura>(`/api/v1/almacen/salidas-rotura/${args.id}/${args.accion}`, {
        method: 'POST',
        body: args.accion === 'rechazar' ? { motivo: args.motivo } : undefined,
        idempotencyKey: crypto.randomUUID(),
      });
      return data;
    },
    // Autorizar baja el saldo: se refrescan también saldos y salidas.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}
