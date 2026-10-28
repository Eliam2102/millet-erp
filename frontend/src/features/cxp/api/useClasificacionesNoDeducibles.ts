import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxpKeys } from '@/features/cxp/api/keys';
import type { ClasificacionNoDeducible } from '@/features/cxp/api/types';

/** Catálogo de clasificaciones de gasto no deducible (P8.6). */
export function useClasificacionesNoDeducibles() {
  return useQuery({
    queryKey: cxpKeys.clasificacionesNoDeducibles(),
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ClasificacionNoDeducible[]>(
        '/api/v1/cuentas-por-pagar/catalogos/clasificaciones-no-deducibles',
        { signal },
      );
      return data;
    },
  });
}
