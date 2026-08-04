import { useQuery } from '@tanstack/react-query';
import { apiFetch } from '@/lib/auth/api-client';
import { ApiError } from '@/lib/api/error';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * <c>usePdfOrdenCompra(id)</c> — descarga el PDF de la OC como Blob y
 * devuelve un object URL (<c>blob:http://...</c>) consumible por
 * <c>&lt;embed src&gt;</c> + <c>&lt;a download&gt;</c>.
 *
 * <para>El endpoint backend (<c>GET /{id}/pdf</c>) requiere Bearer
 * token, así que NO podemos pasar la URL del endpoint directo al
 * <c>&lt;embed&gt;</c> (el browser no agrega el header). En su lugar:
 * fetch via <c>apiFetch</c> (que inyecta el Bearer), convertir la
 * respuesta a Blob, generar object URL.</para>
 *
 * <para><b>Cleanup</b>: el hook devuelve el object URL como string;
 * el caller (componente) debe revocar via <c>URL.revokeObjectURL</c>
 * en cleanup del effect cuando el componente se desmonta o cambia el
 * id. TanStack Query maneja el caching del request HTTP, no del Blob —
 * cada mount genera una nueva URL para evitar memory leaks largos.</para>
 *
 * <para>Solo se ejecuta si <c>autorizada=true</c> — caso contrario el
 * backend devuelve 404 OC_PDF_NO_ENCONTRADO. El componente
 * <c>&lt;TabPdf/&gt;</c> usa esto para mostrar un banner cuando la OC
 * aún no está autorizada en lugar de hacer una request que sabe que
 * fallará.</para>
 */
export interface UsePdfOrdenCompraOpts {
  /** Si <c>false</c>, el query no se ejecuta. Default <c>true</c>. */
  enabled?: boolean;
}

export function usePdfOrdenCompra(
  id: string,
  opts: UsePdfOrdenCompraOpts = {},
) {
  const enabled = opts.enabled !== false;

  return useQuery({
    queryKey: [...ordenesKeys.detail(id), 'pdf'] as const,
    enabled,
    // No revalidar el blob automáticamente — el PDF es inmutable post-
    // autorización (F6-PR1 lo genera al autorizar N2).
    staleTime: Infinity,
    gcTime: 5 * 60 * 1000, // 5 min — libera el blob de memoria pasado un rato.
    retry: false, // 404 no debe reintentar.
    queryFn: async ({ signal }) => {
      const response = await apiFetch(`/api/v1/compras/ordenes/${id}/pdf`, {
        signal,
      });
      if (!response.ok) {
        // Intentar parsear ProblemDetails para ApiError tipado.
        let problem;
        try {
          problem = await response.json();
        } catch {
          problem = {
            type: 'about:blank',
            title: response.statusText || 'Error al descargar PDF',
            status: response.status,
            code: 'PDF_FETCH_ERROR',
          };
        }
        throw new ApiError(problem, response.status);
      }
      const blob = await response.blob();
      return URL.createObjectURL(blob);
    },
  });
}
