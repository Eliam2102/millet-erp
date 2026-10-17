import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

interface ConteoPorEstadoItem {
  estado: number;
  total: number;
}

/**
 * Conteos por estado de una bandeja (<c>GET {ruta}/conteos-por-estado</c>).
 * <c>filtros</c> son los de la bandeja SIN estado ni paginación. La llave
 * cuelga de la de la bandeja (<c>claveBase</c>) para que las mutaciones que
 * invalidan el listado refresquen también los tabs.
 */
export function useConteosPorEstado(
  claveBase: readonly unknown[],
  ruta: string,
  filtros: object,
) {
  return useQuery({
    queryKey: [...claveBase, 'conteos-por-estado', filtros],
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      for (const [k, v] of Object.entries(filtros)) {
        if (v !== undefined && v !== null && v !== '') params.set(k, String(v));
      }
      const { data } = await apiRequest<ConteoPorEstadoItem[]>(
        `${ruta}/conteos-por-estado?${params.toString()}`,
        { signal },
      );
      return Object.fromEntries(data.map((d) => [d.estado, d.total])) as Record<number, number>;
    },
    placeholderData: keepPreviousData,
  });
}
