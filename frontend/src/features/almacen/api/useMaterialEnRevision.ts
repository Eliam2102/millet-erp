import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';
import type { MaterialEnRevisionItem } from '@/features/almacen/api/types';

export interface MaterialEnRevisionFiltros {
  soloPendientes: boolean;
  q?: string;
}

export function useMaterialEnRevision(filtros: MaterialEnRevisionFiltros) {
  return useQuery({
    queryKey: almacenKeys.revision(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ soloPendientes: String(filtros.soloPendientes) });
      if (filtros.q) params.set('q', filtros.q);
      const { data } = await apiRequest<MaterialEnRevisionItem[]>(
        `/api/v1/almacen/revision?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
}
