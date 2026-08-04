import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type { FormaPagoItem } from '@/modules/catalogos/api/types';

/**
 * Hook del catálogo SAT Formas de Pago (UF-Admin-PR5.3). Read-only,
 * mantenido vía migración del backend (catálogo SAT). Sin paginación.
 */
export function useFormasPago() {
  return useQuery({
    queryKey: catalogosKeys.formasPago(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<FormaPagoItem[]>(
        '/api/v1/catalogos/formas-pago',
        { signal },
      );
      return data;
    },
    staleTime: 60 * 60 * 1000, // 1h — catálogo SAT inmutable en sesión
    gcTime: 2 * 60 * 60 * 1000,
  });
}
