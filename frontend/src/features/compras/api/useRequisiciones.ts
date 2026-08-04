import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  comprasKeys,
  type ListarRequisicionesFiltros,
} from '@/features/compras/api/keys';
import type {
  PagedResponse,
  RequisicionListItemResponse,
} from '@/features/compras/api/types';

/**
 * Lista paginada de requisiciones de la empresa actual del JWT, con
 * filtros opcionales (estado, departamento, requisitante, búsqueda
 * por folio, paginación offset-based). Doc 05 §7.2.
 *
 * <para>El backend ya filtra por <c>EmpresaId</c> del JWT (multi-tenant
 * ADR-0011); el frontend no agrega filtros de seguridad. Si el usuario
 * no tiene <c>compras.requisiciones.ver-todos-departamentos</c>, el
 * caller (BandejaRequisiciones page) inyecta automáticamente
 * <c>departamentoId = me.departamentoId</c>.</para>
 *
 * @example
 * ```tsx
 * const { data, isLoading } = useRequisiciones({ estado: EstadoRequisicion.EnAutorizacion });
 * ```
 */
export function useRequisiciones(filtros: ListarRequisicionesFiltros = {}) {
  return useQuery({
    queryKey: comprasKeys.requisicionesList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarPath(filtros);
      const { data } = await apiRequest<
        PagedResponse<RequisicionListItemResponse>
      >(path, { signal });
      return data;
    },
    // staleTime: hereda del default global (30s). Mutations posteriores
    // (Crear/Eliminar/Transmitir/...) invalidan explícitamente.
  });
}

/**
 * Construye el path con query params solo para los filtros realmente
 * presentes (omite <c>undefined</c>/<c>null</c>/<c>''</c>). Mantiene
 * la URL limpia para que el cache key y el log de red sean estables.
 */
function buildListarPath(filtros: ListarRequisicionesFiltros): string {
  const params = new URLSearchParams();
  if (filtros.estado != null) params.set('estado', String(filtros.estado));
  if (filtros.departamentoId)
    params.set('departamentoId', filtros.departamentoId);
  if (filtros.requisitanteId)
    params.set('requisitanteId', filtros.requisitanteId);
  if (filtros.q) params.set('q', filtros.q);
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));

  const query = params.toString();
  return query
    ? `/api/v1/compras/requisiciones?${query}`
    : '/api/v1/compras/requisiciones';
}
