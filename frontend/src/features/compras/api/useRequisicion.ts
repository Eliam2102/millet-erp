import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { RequisicionResponse } from '@/features/compras/api/types';

/**
 * Forma del valor cacheado internamente: el hook captura el ETag al
 * lado del payload para que las mutations posteriores lo manden como
 * <c>If-Match</c> (ADR-0012 Capa 1, doc 05 §7.5). El consumidor del
 * hook ve solo <c>data: RequisicionResponse</c> gracias a <c>select</c>;
 * el etag se lee con <see cref="useEtag"/>.
 */
interface CachedRequisicion {
  data: RequisicionResponse;
  etag: string | undefined;
}

/**
 * <c>useRequisicion(id)</c> — detalle completo de una RQ por id.
 * Captura el <c>ETag</c> emitido por el backend (mirror del
 * <c>Version</c> del agregado) y lo guarda en la cache de TanStack
 * Query envuelto con el payload (<c>{ data, etag }</c>); el
 * <c>select</c> expone solo <c>data</c> al consumidor para que el
 * shape público sea <c>RequisicionResponse</c>.
 *
 * <para>Comportamiento:</para>
 * <list>
 *   <item><c>staleTime: 0</c> — fresh en cada mount, porque la página
 *   de detalle (P3) muestra datos potencialmente cambiantes (otras
 *   firmas, recepciones).</item>
 *   <item>4xx (404/403) NO se reintentan (heredado del
 *   <c>queryClient</c>); el caller renderiza la página de
 *   permission-denied o not-found (doc 05 §13.6).</item>
 *   <item>El <c>id</c> puede ser <c>null</c> si la página todavía no
 *   tiene id; el hook se inhabilita automáticamente
 *   (<c>enabled: id != null</c>).</item>
 * </list>
 */
export function useRequisicion(id: string | null | undefined) {
  return useQuery<CachedRequisicion, Error, RequisicionResponse>({
    queryKey:
      id != null ? comprasKeys.requisicion(id) : (['compras', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) {
        throw new Error('useRequisicion invocado sin id');
      }
      const { data, etag } = await apiRequest<RequisicionResponse>(
        `/api/v1/compras/requisiciones/${id}`,
        { signal },
      );
      return { data, etag };
    },
    select: (cached) => cached.data,
    enabled: id != null,
    staleTime: 0,
  });
}

/**
 * <c>useEtag(id)</c> — lee el <c>ETag</c> capturado por
 * <see cref="useRequisicion"/>. Devuelve <c>undefined</c> si la query
 * nunca se ejecutó o si el backend no emitió ETag.
 *
 * <para>Las mutations lo consumen para mandar <c>If-Match</c>:</para>
 *
 * @example
 * ```tsx
 * const etag = useEtag(rq.id);
 * autorizar.mutate({ rqId: rq.id, ifMatch: etag });
 * ```
 */
export function useEtag(id: string | null | undefined): string | undefined {
  const queryClient = useQueryClient();
  if (id == null) return undefined;
  const cached = queryClient.getQueryData<CachedRequisicion>(
    comprasKeys.requisicion(id),
  );
  return cached?.etag;
}
