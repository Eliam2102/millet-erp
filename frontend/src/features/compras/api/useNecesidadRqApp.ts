import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { NecesidadRqAppResponse } from '@/features/compras/api/types';

/** M-332: necesidad / disponible / convertido a OC / pendiente de una RQ de la app de Almacenes (solo lectura). */
export function useNecesidadRqApp(requisicionId: string, enabled: boolean) {
  return useQuery({
    queryKey: comprasKeys.necesidadApp(requisicionId),
    enabled,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<NecesidadRqAppResponse>(
        `/api/v1/compras/requisiciones/${requisicionId}/necesidad-app`,
        { signal },
      );
      return data;
    },
  });
}
