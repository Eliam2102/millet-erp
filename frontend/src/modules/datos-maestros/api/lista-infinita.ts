import { useMemo } from 'react';
import { useInfiniteQuery, type QueryKey } from '@tanstack/react-query';

/** Tamaño de página de los listados de Datos Maestros (máximo que acepta la API). */
export const PAGINA_MAESTRO = 200;

interface Pagina<T> {
  items: readonly T[];
  total: number;
}

/**
 * Listado con «Cargar más» sobre el <c>offset</c>/<c>limit</c> de la API: junta las
 * páginas pedidas y expone el <c>total</c> del universo filtrado. La key hereda la del
 * listado y añade <c>'infinita'</c>, así sigue bajo el prefijo que invalidan las mutaciones.
 */
export function useListaInfinita<T>(
  queryKey: QueryKey,
  fetchPagina: (offset: number, signal: AbortSignal) => Promise<Pagina<T>>,
) {
  const query = useInfiniteQuery({
    queryKey: [...queryKey, 'infinita'],
    initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => fetchPagina(pageParam, signal),
    getNextPageParam: (ultima, todas) => {
      const cargados = todas.reduce((s, p) => s + p.items.length, 0);
      return ultima.items.length > 0 && cargados < ultima.total ? cargados : undefined;
    },
    staleTime: 30_000,
  });
  const items = useMemo(() => query.data?.pages.flatMap((p) => p.items) ?? [], [query.data]);
  return { query, items, total: query.data?.pages[0]?.total ?? 0 };
}
