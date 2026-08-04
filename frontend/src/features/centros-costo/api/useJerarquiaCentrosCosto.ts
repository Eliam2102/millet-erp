import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { centrosCostoKeys, type HijosJerarquiaCeCoParams } from './keys';
import type { NodoCeCo } from './types';

/**
 * Hijos inmediatos de un nodo del árbol de configuración
 * (<c>GET /api/v1/centros-costo/jerarquia</c>, CECO-PR3/PR4): carga
 * perezosa — una llamada acotada por expansión, cacheada por nodo. Los
 * conteos (<c>dim2Vivas</c>/<c>dim3Vivas</c>) vienen calculados del
 * backend con el MISMO predicado de la cascada (ADR-0049); el FE no
 * cuenta nada.
 */
export function useHijosJerarquiaCentrosCosto(params: HijosJerarquiaCeCoParams) {
  return useQuery({
    queryKey: centrosCostoKeys.jerarquia(params),
    queryFn: async ({ signal }) => {
      const query = new URLSearchParams({ nodoTipo: params.nodoTipo });
      if (params.nodoId) query.set('nodoId', params.nodoId);
      if (params.incluirInactivos) query.set('incluirInactivos', 'true');
      const { data } = await apiRequest<NodoCeCo[]>(
        `/api/v1/centros-costo/jerarquia?${query.toString()}`,
        { signal },
      );
      return data;
    },
  });
}
