import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';

/**
 * Forma del valor cacheado internamente: el hook captura el ETag al
 * lado del payload para que las mutations posteriores lo manden como
 * <c>If-Match</c> (ADR-0012 Capa 1, doc 05 §7.5). El consumidor del
 * hook ve solo <c>data: OrdenCompraDetalleResponse</c> gracias a
 * <c>select</c>; el etag se lee con <see cref="useEtagOc"/>.
 */
interface CachedOrdenCompra {
  data: OrdenCompraDetalleResponse;
  etag: string | undefined;
}

/**
 * <c>useOrdenCompra(id)</c> — detalle (cabecera) de una OC por id.
 * Captura el <c>ETag</c> emitido por el backend (mirror del
 * <c>Version</c> del agregado) y lo guarda en la cache de TanStack
 * Query envuelto con el payload (<c>{ data, etag }</c>); el
 * <c>select</c> expone solo <c>data</c> al consumidor para que el
 * shape público sea <c>OrdenCompraDetalleResponse</c>.
 *
 * <para>Comportamiento (heredado de <c>useRequisicion</c>):</para>
 * <list>
 *   <item><c>staleTime: 0</c> — fresh en cada mount, porque la página
 *   de detalle (P3) muestra datos potencialmente cambiantes (sub-estados
 *   que avanzan con recepciones/facturas/pagos).</item>
 *   <item>4xx (404/403) NO se reintentan (heredado del
 *   <c>queryClient</c>); el caller renderiza la página de
 *   permission-denied o not-found.</item>
 *   <item>El <c>id</c> puede ser <c>null</c> si la página todavía no
 *   tiene id; el hook se inhabilita automáticamente
 *   (<c>enabled: id != null</c>).</item>
 * </list>
 */
export function useOrdenCompra(id: string | null | undefined) {
  return useQuery<CachedOrdenCompra, Error, OrdenCompraDetalleResponse>({
    queryKey:
      id != null
        ? ordenesKeys.detail(id)
        : (['compras', 'ordenes', 'noop'] as const),
    queryFn: async ({ signal }) => {
      if (id == null) {
        throw new Error('useOrdenCompra invocado sin id');
      }
      const { data, etag } = await apiRequest<OrdenCompraDetalleResponse>(
        `/api/v1/compras/ordenes/${id}`,
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
 * <c>useEtagOc(id)</c> — lee el <c>ETag</c> capturado por
 * <see cref="useOrdenCompra"/>. Devuelve <c>undefined</c> si la query
 * nunca se ejecutó o si el backend no emitió ETag.
 *
 * <para>Las mutations de OC (UF2 en adelante) lo consumen para mandar
 * <c>If-Match</c>:</para>
 *
 * @example
 * ```tsx
 * const etag = useEtagOc(oc.id);
 * autorizar.mutate({ ocId: oc.id, ifMatch: etag });
 * ```
 */
export function useEtagOc(id: string | null | undefined): string | undefined {
  const queryClient = useQueryClient();
  if (id == null) return undefined;
  const cached = queryClient.getQueryData<CachedOrdenCompra>(
    ordenesKeys.detail(id),
  );
  return cached?.etag;
}
