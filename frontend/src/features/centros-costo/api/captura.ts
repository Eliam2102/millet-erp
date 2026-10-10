import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
export interface CentroCostoOpcion {
  id: string; clave: string; nombre: string; nivel: number; activo: boolean;
  dim1Id: string; dim2Id: string | null;
}
export interface CentroCostoCaptura {
  heredado: CentroCostoOpcion | null; puedeElegir: boolean;
  unicaOpcion: CentroCostoOpcion | null; mensaje: string | null;
}
export function useCentroCostoCaptura(requisicionId: string) {
  return useQuery({
    queryKey: ['compras', 'rq', requisicionId, 'centro-costo-captura'],
    queryFn: async ({ signal }) => (await apiRequest<CentroCostoCaptura>(
      `/api/v1/compras/requisiciones/${requisicionId}/lineas/centro-costo-captura`, { signal })).data,
  });
}
