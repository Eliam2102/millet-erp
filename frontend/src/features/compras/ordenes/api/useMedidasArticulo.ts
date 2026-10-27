import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { MedidaHojaArticulo } from '@/features/compras/ordenes/api/types';

/**
 * <c>useMedidasArticulo(articuloId)</c> — GET
 * <c>/api/v1/catalogos/articulos/{id}/medidas</c> (F1-MP-01, E1). Lista vacía = el
 * artículo no está ligado a un producto A+W (no maneja medidas de hoja).
 */
export function useMedidasArticulo(articuloId: string | null | undefined) {
  return useQuery({
    queryKey: [...ordenesKeys.all, 'medidas-articulo', articuloId ?? 'none'] as const,
    enabled: !!articuloId,
    staleTime: 60_000,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<MedidaHojaArticulo[]>(
        `/api/v1/catalogos/articulos/${articuloId}/medidas`,
        { signal },
      );
      return data;
    },
  });
}
