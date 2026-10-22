import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';

/** Existencia apartada de una línea de RQ (solo líneas con reserva). */
export interface ApartadoLineaResponse {
  lineaId: string;
  pendiente: number;
  liberada: number;
  unidadMedida: string;
  ultimoMotivo: string | null;
}

export function useApartadosRequisicion(requisicionId: string, enabled = true) {
  return useQuery({
    queryKey: comprasKeys.apartados(requisicionId),
    enabled,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ApartadoLineaResponse[]>(
        `/api/v1/compras/requisiciones/${requisicionId}/apartados`,
        { signal },
      );
      return data;
    },
  });
}

export interface LiberarApartadoArgs {
  requisicionId: string;
  lineaId: string;
  values: { cantidad: number; motivo: string };
  idempotencyKey: string;
}

/** Libera (parcial o totalmente) el apartado de una línea. 204; Idempotency-Key obligatorio. */
export function useLiberarApartado() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, LiberarApartadoArgs>({
    mutationFn: async ({ requisicionId, lineaId, values, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/lineas/${lineaId}/liberar-apartado`,
        { method: 'POST', body: values, idempotencyKey },
      );
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: comprasKeys.all }),
  });
}
