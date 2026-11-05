import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/** A4.4: desperdicio de mesa de corte y merma por etapa que informa A+W (espejo de `MermaProcesoResponse`). */
export interface MermaProceso {
  id: string;
  sucursalId: string;
  tipo: 'DESPERDICIO_MESA' | 'MERMA';
  fecha: string;
  mesa: string | null;
  etapa: string;
  motivo: string;
  articuloId: string;
  articuloClave: string | null;
  articuloDescripcion: string | null;
  kg: number | null;
  m2: number;
  costoUnitarioM2Mxn: number;
  valorMxn: number;
  estado: 'PENDIENTE' | 'APLICADA' | 'RECHAZADA';
  autorizadoPor: string | null;
  autorizadoAt: string | null;
  motivoRechazo: string | null;
}

export function useMermasProceso(filtros: { estado?: string; tipo?: string }) {
  return useQuery({
    queryKey: almacenKeys.mermas(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado) params.set('estado', filtros.estado);
      if (filtros.tipo) params.set('tipo', filtros.tipo);
      const { data } = await apiRequest<MermaProceso[]>(
        `/api/v1/almacen/mermas-proceso?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
}

export function useDecidirMerma() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; accion: 'autorizar' | 'rechazar'; motivo?: string }) => {
      const { data } = await apiRequest<MermaProceso>(
        `/api/v1/almacen/mermas-proceso/${args.id}/${args.accion}`,
        {
          method: 'POST',
          body: args.accion === 'rechazar' ? { motivo: args.motivo } : undefined,
          idempotencyKey: crypto.randomUUID(),
        },
      );
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}
