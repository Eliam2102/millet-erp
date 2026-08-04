import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type { UsoCfdiItem } from '@/modules/catalogos/api/types';

/**
 * Hook del catálogo SAT Usos CFDI (UF-Admin-PR5.3). Read-only.
 */
export function useUsosCfdi() {
  return useQuery({
    queryKey: catalogosKeys.usosCfdi(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<UsoCfdiItem[]>(
        '/api/v1/catalogos/usos-cfdi',
        { signal },
      );
      return data;
    },
    staleTime: 60 * 60 * 1000,
    gcTime: 2 * 60 * 60 * 1000,
  });
}
