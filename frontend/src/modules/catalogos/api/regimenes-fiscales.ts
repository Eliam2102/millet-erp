import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type { RegimenFiscalItem } from '@/modules/catalogos/api/types';

/**
 * Hook del catálogo SAT Regímenes Fiscales (UF-Admin-PR5.3). Read-only.
 * Endpoint vive en <c>CatalogosOcEndpoints</c> (no en
 * <c>CatalogosSatEndpoints</c>) — pre-existente desde F9-PR1, lo
 * consumimos sin filtros para la admin UI (lista completa, ambos
 * tipos persona).
 */
export function useRegimenesFiscales() {
  return useQuery({
    queryKey: catalogosKeys.regimenesFiscales(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<RegimenFiscalItem[]>(
        '/api/v1/catalogos/regimenes-fiscales',
        { signal },
      );
      return data;
    },
    staleTime: 60 * 60 * 1000,
    gcTime: 2 * 60 * 60 * 1000,
  });
}
