import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type { ConciliacionCobrosResponse } from '@/features/cxc/api/types';

/** <c>useConciliacionPolizas(desde, hasta)</c> — CXC-06: cobros aplicados en cartera contra sus pólizas. */
export function useConciliacionPolizas(desde: string, hasta: string) {
  return useQuery<ConciliacionCobrosResponse>({
    queryKey: cxcKeys.conciliacionPolizas(desde, hasta),
    enabled: desde !== '' && hasta !== '',
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ desde, hasta });
      const { data } = await apiRequest<ConciliacionCobrosResponse>(
        `/api/v1/cuentas-por-cobrar/cartera/conciliacion-polizas?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
}
