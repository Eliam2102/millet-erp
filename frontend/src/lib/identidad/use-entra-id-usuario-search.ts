import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

export interface EntraIdUsuarioSearchResult {
  /** ObjectId del usuario en Microsoft Entra ID. */
  objectId: string;
  /** Nombre legible (<c>displayName</c> de Graph). */
  nombre: string;
  /** Email primario reportado por Entra ID. */
  email: string;
}

export interface UseEntraIdUsuarioSearchResult {
  data: EntraIdUsuarioSearchResult | undefined;
  isLoading: boolean;
  isError: boolean;
  isFetched: boolean;
}

export function useEntraIdUsuarioSearch(
  email: string | null | undefined,
): UseEntraIdUsuarioSearchResult {
  const normalizado = email?.trim().toLowerCase() ?? '';
  const query = useQuery({
    queryKey: ['identidad', 'entra', normalizado],
    enabled: /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(normalizado),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<{
        encontrado: boolean;
        objectId: string | null;
        nombre: string | null;
      }>(`/api/v1/identidad/usuarios/entra?email=${encodeURIComponent(normalizado)}`, { signal });
      return data.encontrado && data.objectId
        ? { objectId: data.objectId, nombre: data.nombre ?? '', email: normalizado }
        : undefined;
    },
    staleTime: 30_000,
  });
  return { data: query.data, isLoading: query.isLoading && query.fetchStatus === 'fetching', isError: query.isError, isFetched: query.isFetched };
}
