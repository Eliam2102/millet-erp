import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { centrosCostoKeys } from '@/features/centros-costo/api/keys';
import type { Dim3BusquedaItem } from '@/features/centros-costo/api/types';

const ENDPOINT = '/api/v1/centros-costo/dim3/buscar';

/**
 * Prellenado del CC-Máquina en la línea de RQ (Fase E PR2, F1 Card 2): si el
 * alcance del usuario resuelve a **exactamente 1** máquina, la devuelve para
 * prellenar; **0** (bloqueo esperado — sin máquinas asignadas) o **≥2** → null
 * (el usuario elige). Reusa el selector FILTRADO por alcance (`/dim3/buscar`):
 * con `limit=2` basta para distinguir "1" de "≥2" sin traer las ~361.
 *
 * <para>Se activa solo en modo AGREGAR (`enabled`), nunca en edición (la línea
 * ya trae su valor). El resultado se cachea (`staleTime`) para no re-pegar al
 * endpoint por cada línea agregada.</para>
 */
export function useCcMaquinaPrellenado(enabled: boolean): {
  maquina: Dim3BusquedaItem | null;
  isLoading: boolean;
} {
  const query = useQuery({
    queryKey: centrosCostoKeys.buscarDim3(ENDPOINT, { prellenado: true }),
    enabled,
    staleTime: 60_000,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<Dim3BusquedaItem[]>(
        `${ENDPOINT}?limit=2`,
        { signal },
      );
      return data;
    },
  });

  const items = query.data;
  return {
    // Exactamente 1 → prellena; 0 o ≥2 → null.
    maquina: items && items.length === 1 ? items[0] : null,
    isLoading: query.isLoading,
  };
}
