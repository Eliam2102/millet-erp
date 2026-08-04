import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  type NodoArbolDocumento,
  type TipoDocumentoTrazabilidad,
} from '@/components/erp/trazabilidad/types';

/**
 * <c>useArbolDocumentos(tipo, id)</c> — wrapper del endpoint cross-
 * módulo <c>GET /api/v1/compras/trazabilidad/arbol-documentos</c>
 * (UF7-PR2). Vive en <c>features/compras/ordenes/api/</c> porque OC
 * es el primer consumidor; cuando otros módulos lo necesiten, considerar
 * promover a <c>features/trazabilidad/api/</c>.
 *
 * <para>Permiso: <c>compras.ordenes.leer</c>. Devuelve <c>null</c> si
 * el documento no existe (404). Cache moderado (1 min) — el árbol
 * cambia cuando se crean/cancelan documentos relacionados.</para>
 */
export function useArbolDocumentos(
  tipo: TipoDocumentoTrazabilidad,
  id: string,
  opts: { enabled?: boolean } = {},
) {
  const enabled = opts.enabled !== false && !!id;

  return useQuery({
    queryKey: ['compras', 'trazabilidad', 'arbol-documentos', tipo, id] as const,
    enabled,
    staleTime: 60_000,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({
        desde: String(tipo),
        id,
      });
      const { data } = await apiRequest<NodoArbolDocumento>(
        `/api/v1/compras/trazabilidad/arbol-documentos?${params}`,
        { signal },
      );
      return data;
    },
  });
}
