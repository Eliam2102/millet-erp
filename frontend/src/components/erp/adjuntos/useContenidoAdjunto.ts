import { useQuery } from '@tanstack/react-query';
import { apiFetch } from '@/lib/auth/api-client';

/**
 * <c>useContenidoAdjunto(endpointPath)</c> — baja el contenido de un
 * blob SUBIDO desde un endpoint autenticado del backend (Bearer vía
 * <c>apiFetch</c>) y devuelve un object URL (<c>blob:http://…</c>) apto
 * para <c>&lt;embed&gt;</c>/<c>&lt;img&gt;</c>/<c>&lt;a download&gt;</c>.
 *
 * <para>Mismo patrón que <c>usePdfOrdenCompra</c>/<c>useValeBlob</c>: el
 * endpoint requiere Bearer, así que NO se puede pasar la URL del endpoint
 * directo a <c>&lt;embed&gt;</c> (el browser no agrega el header). Y NUNCA
 * se usa el blobUrl crudo del storage (<c>file://</c> en dev no es
 * navegable desde una página http). ADR-0024.</para>
 *
 * <para><c>gcTime: 0</c> — cada fila baja su propio adjunto y revoca el
 * object URL al desmontar (ver <c>AdjuntoFila</c>); sin cache evitamos
 * devolver un object URL ya revocado en un remonte. <c>enabled</c> queda
 * apagado si <paramref name="endpointPath"/> es <c>null</c> (lazy por
 * adjunto; no se bajan todos en un solo request).</para>
 *
 * @param endpointPath Ruta del endpoint de contenido, o <c>null</c> para
 *   no bajar nada.
 */
export function useContenidoAdjunto(endpointPath: string | null) {
  return useQuery({
    queryKey: ['adjunto-contenido', endpointPath] as const,
    enabled: endpointPath != null,
    staleTime: Infinity,
    gcTime: 0,
    retry: false,
    queryFn: async ({ signal }) => {
      const response = await apiFetch(endpointPath as string, { signal });
      if (!response.ok) {
        throw new Error(
          `No se pudo cargar el contenido del adjunto (HTTP ${response.status}).`,
        );
      }
      const blob = await response.blob();
      return URL.createObjectURL(blob);
    },
  });
}
