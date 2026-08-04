import { useQuery } from '@tanstack/react-query';
import { apiFetch } from '@/lib/auth/api-client';
import { ApiError } from '@/lib/api/error';
import { almacenKeys } from '@/features/almacen/api/keys';

/**
 * <c>useValeBlob(salidaId, enabled)</c> — descarga el vale escaneado de
 * una salida como Blob y devuelve un object URL (<c>blob:http://...</c>)
 * apto para <c>&lt;a href&gt;</c>, <c>&lt;embed src&gt;</c> o
 * <c>window.open</c>.
 *
 * <para>Mismo patrón que <c>usePdfOrdenCompra</c>: el endpoint backend
 * (<c>GET /salidas/{id}/vale</c>) requiere Bearer, no podemos pasar la
 * URL directa al browser (no agrega el header). Fetcheamos via
 * <c>apiFetch</c>, convertimos a Blob, generamos object URL.</para>
 *
 * <para>Cleanup: el caller debe revocar via <c>URL.revokeObjectURL</c>
 * en cleanup del effect. <c>gcTime: 5min</c> libera el blob de memoria
 * pasado un rato sin uso.</para>
 *
 * <para>Solo se ejecuta cuando <c>enabled=true</c> (ej. salida es por
 * vale Y tiene <c>valeBlobRef</c>). Caso contrario el backend devuelve
 * 422 SALIDA_SIN_VALE.</para>
 */
export interface UseValeBlobOpts {
  enabled?: boolean;
}

export function useValeBlob(
  salidaId: string,
  opts: UseValeBlobOpts = {},
) {
  const enabled = opts.enabled !== false;

  return useQuery({
    queryKey: [...almacenKeys.salidaById(salidaId), 'vale-blob'] as const,
    enabled,
    staleTime: Infinity,
    gcTime: 5 * 60 * 1000,
    retry: false,
    queryFn: async ({ signal }) => {
      const response = await apiFetch(
        `/api/v1/almacen/salidas/${salidaId}/vale`,
        { signal },
      );
      if (!response.ok) {
        let problem;
        try {
          problem = await response.json();
        } catch {
          problem = {
            type: 'about:blank',
            title: response.statusText || 'Error al descargar vale',
            status: response.status,
            code: 'VALE_FETCH_ERROR',
          };
        }
        throw new ApiError(problem, response.status);
      }
      const blob = await response.blob();
      return URL.createObjectURL(blob);
    },
  });
}
