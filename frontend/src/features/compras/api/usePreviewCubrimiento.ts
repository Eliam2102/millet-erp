import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { PreviewCubrimientoResponse } from '@/features/compras/api/types';

/**
 * <c>usePreviewCubrimiento(rqId, enabled)</c> — preview read-only del
 * cubrimiento estimado de una RQ (PR-C). Lazy: el caller pasa
 * <c>enabled = (estado === EnAutorizacion)</c> para que solo se consulte el
 * stock en ese estado (en otros, el cubrimiento real ya está persistido y el
 * backend devolvería <c>aplica=false</c>).
 *
 * <para>NO reserva nada: es informativo, estimación sujeta a disponibilidad
 * hasta autorizar.</para>
 */
export function usePreviewCubrimiento(requisicionId: string, enabled: boolean) {
  return useQuery({
    queryKey: comprasKeys.cubrimientoEstimado(requisicionId),
    enabled,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<PreviewCubrimientoResponse>(
        `/api/v1/compras/requisiciones/${requisicionId}/cubrimiento-estimado`,
        { signal },
      );
      return data;
    },
  });
}
