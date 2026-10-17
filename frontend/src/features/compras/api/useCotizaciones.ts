import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type {
  ComparativoCotizaciones,
  CotizacionGuardada,
  RegistrarCotizacionRequest,
} from '@/features/compras/api/types';

/** Hooks de cotizaciones de una requisición (F1-COM-02). */

const baseUrl = (requisicionId: string) =>
  `/api/v1/compras/requisiciones/${requisicionId}/cotizaciones`;

/** <c>GET /api/v1/compras/requisiciones/{id}/cotizaciones</c> — comparativo normalizado. */
export function useComparativoCotizaciones(requisicionId: string | undefined) {
  return useQuery({
    queryKey: comprasKeys.cotizaciones(requisicionId ?? ''),
    enabled: !!requisicionId,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ComparativoCotizaciones>(
        baseUrl(requisicionId!),
        { signal },
      );
      return data;
    },
  });
}

export interface RegistrarCotizacionArgs {
  values: RegistrarCotizacionRequest;
  idempotencyKey: string;
}

/** <c>POST /api/v1/compras/requisiciones/{id}/cotizaciones</c>. Idempotency-Key obligatorio (ADR-0020). */
export function useRegistrarCotizacion(requisicionId: string) {
  const queryClient = useQueryClient();
  return useMutation<CotizacionGuardada, Error, RegistrarCotizacionArgs>({
    mutationFn: async ({ values, idempotencyKey }) => {
      const { data } = await apiRequest<CotizacionGuardada>(baseUrl(requisicionId), {
        method: 'POST',
        body: values,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: comprasKeys.cotizaciones(requisicionId),
      }),
  });
}
