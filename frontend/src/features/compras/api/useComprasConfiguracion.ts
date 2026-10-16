import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type {
  ComprasConfiguracionResponse,
  ModoNivel2,
} from '@/features/compras/api/types';

/**
 * Hooks de la configuración del módulo Compras (<c>/api/v1/compras/configuracion</c>).
 */

/** <c>GET /api/v1/compras/configuracion</c>. */
export function useComprasConfiguracion() {
  return useQuery({
    queryKey: comprasKeys.configuracion(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ComprasConfiguracionResponse>(
        '/api/v1/compras/configuracion',
        { signal },
      );
      return data;
    },
  });
}

export interface ActualizarModoNivel2Args {
  modoNivel2: ModoNivel2;
  idempotencyKey: string;
}

/**
 * <c>PATCH /api/v1/compras/configuracion</c> con solo <c>modoNivel2</c>
 * (PATCH parcial: los demás campos no se tocan). Idempotency-Key obligatorio.
 */
export function useActualizarModoNivel2() {
  const queryClient = useQueryClient();
  return useMutation<ComprasConfiguracionResponse, Error, ActualizarModoNivel2Args>({
    mutationFn: async ({ modoNivel2, idempotencyKey }) => {
      const { data } = await apiRequest<ComprasConfiguracionResponse>(
        '/api/v1/compras/configuracion',
        { method: 'PATCH', body: { modoNivel2 }, idempotencyKey },
      );
      return data;
    },
    onSuccess: (resp) => {
      queryClient.setQueryData(comprasKeys.configuracion(), resp);
    },
  });
}
