import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type {
  UmbralAprobacionResponse,
  UpsertUmbralRequest,
} from '@/features/compras/api/types';

/**
 * Hooks de umbrales de aprobación N1/N2 por departamento (motor de
 * autorización §3.bis.2).
 *
 * <para>Convención de invalidación: tras guardar, se invalida
 * <c>comprasKeys.umbralesAprobacion()</c> para que la tabla refleje la
 * vigencia nueva (el backend versiona la fila del día).</para>
 */

/** <c>GET /api/v1/compras/umbrales-aprobacion</c> — umbrales vigentes de la empresa. */
export function useUmbralesAprobacion() {
  return useQuery({
    queryKey: comprasKeys.umbralesAprobacion(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<UmbralAprobacionResponse[]>(
        '/api/v1/compras/umbrales-aprobacion',
        { signal },
      );
      return data;
    },
  });
}

export interface GuardarUmbralArgs {
  values: UpsertUmbralRequest;
  idempotencyKey: string;
}

/**
 * <c>PUT /api/v1/compras/umbrales-aprobacion</c>. Idempotency-Key
 * obligatorio (ADR-0020). El backend responde 400 si N1 &gt;= N2; el
 * error llega tal cual al componente para mostrarlo.
 */
export function useGuardarUmbral() {
  const queryClient = useQueryClient();
  return useMutation<UmbralAprobacionResponse, Error, GuardarUmbralArgs>({
    mutationFn: async ({ values, idempotencyKey }) => {
      const { data } = await apiRequest<UmbralAprobacionResponse>(
        '/api/v1/compras/umbrales-aprobacion',
        { method: 'PUT', body: values, idempotencyKey },
      );
      return data;
    },
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: comprasKeys.umbralesAprobacion(),
      }),
  });
}
