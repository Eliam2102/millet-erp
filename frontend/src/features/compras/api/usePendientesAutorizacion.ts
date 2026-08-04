import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  comprasKeys,
  type ListarPendientesFiltros,
} from '@/features/compras/api/keys';
import type {
  PagedResponse,
  RequisicionListItemResponse,
} from '@/features/compras/api/types';

/**
 * <c>usePendientesAutorizacion(filtros)</c> — bandeja P2 (doc 05 §5).
 * Atajo del endpoint <c>GET /pendientes-autorizacion</c> que el
 * backend mapea internamente a
 * <c>requisicionesList({ estado: EnAutorizacion })</c>.
 *
 * <para>Filtros: <c>departamentoId</c> opcional (gateado en UI por
 * <c>ver-todos-departamentos</c>; sin permiso, vista de solo el
 * propio depto), paginación offset-based.</para>
 */
export function usePendientesAutorizacion(
  filtros: ListarPendientesFiltros = {},
) {
  return useQuery({
    queryKey: comprasKeys.pendientesAutorizacion(filtros),
    queryFn: async ({ signal }) => {
      const path = buildPath(filtros);
      const { data } = await apiRequest<
        PagedResponse<RequisicionListItemResponse>
      >(path, { signal });
      return data;
    },
  });
}

function buildPath(filtros: ListarPendientesFiltros): string {
  const params = new URLSearchParams();
  if (filtros.departamentoId)
    params.set('departamentoId', filtros.departamentoId);
  if (filtros.nivelPendiente != null)
    params.set('nivelPendiente', String(filtros.nivelPendiente));
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  const query = params.toString();
  return query
    ? `/api/v1/compras/pendientes-autorizacion?${query}`
    : '/api/v1/compras/pendientes-autorizacion';
}
