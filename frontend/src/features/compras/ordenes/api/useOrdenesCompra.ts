import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  ordenesKeys,
  type ListarOrdenesCompraFiltros,
} from '@/features/compras/ordenes/api/keys';
import type { ListarOrdenesCompraResponse } from '@/features/compras/ordenes/api/types';

/**
 * <c>useOrdenesCompra(filtros)</c> — bandeja paginada de OCs de la
 * empresa actual del JWT, con filtros opcionales (estado, sub-estados,
 * proveedor, comprador, fechas, referencia proveedor, paginación
 * page/pageSize). Mirror de <c>GET /api/v1/compras/ordenes</c>
 * (F6-PR3 backend).
 *
 * <para>El backend ya filtra por <c>EmpresaId</c> del JWT (multi-tenant
 * ADR-0011); el frontend no agrega filtros de seguridad. Default del
 * backend para paginación es <c>page=1</c>, <c>pageSize=50</c> (máx
 * 200); el frontend solo envía los params que están presentes para
 * mantener URL y cache key limpios.</para>
 *
 * @example
 * ```tsx
 * const { data, isLoading } = useOrdenesCompra({
 *   estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
 *   page: 1,
 * });
 * ```
 */
export function useOrdenesCompra(
  filtros: ListarOrdenesCompraFiltros = {},
) {
  return useQuery({
    queryKey: ordenesKeys.list(filtros),
    queryFn: async ({ signal }) => {
      const path = buildListarPath(filtros);
      const { data } = await apiRequest<ListarOrdenesCompraResponse>(path, {
        signal,
      });
      return data;
    },
    // staleTime: hereda del default global (30s). Mutations posteriores
    // (UF2 / UF4 / UF5) invalidan la familia entera con
    // ordenesKeys.all.
  });
}

/**
 * Construye el path con query params solo para los filtros realmente
 * presentes (omite <c>undefined</c>/<c>null</c>/<c>''</c>). Mantiene
 * la URL limpia para que el cache key y el log de red sean estables.
 *
 * <para>Mapeo nombre frontend → backend (ver
 * <c>OrdenesCompraEndpoints.cs</c>): <c>fechaDesde</c> →
 * <c>fechaDesde</c>, <c>fechaHasta</c> → <c>fechaHasta</c> (mismo
 * camelCase). El backend usa <c>DateTimeOffset?</c> que acepta ISO 8601
 * directo.</para>
 */
function buildListarPath(filtros: ListarOrdenesCompraFiltros): string {
  const params = new URLSearchParams();
  if (filtros.estado != null) params.set('estado', String(filtros.estado));
  if (filtros.subEstadoRecepcion != null)
    params.set('subEstadoRecepcion', String(filtros.subEstadoRecepcion));
  if (filtros.soloConPendienteRecepcion)
    params.set('soloConPendienteRecepcion', 'true');
  if (filtros.subEstadoFacturacion != null)
    params.set('subEstadoFacturacion', String(filtros.subEstadoFacturacion));
  if (filtros.subEstadoPago != null)
    params.set('subEstadoPago', String(filtros.subEstadoPago));
  if (filtros.proveedorId) params.set('proveedorId', filtros.proveedorId);
  if (filtros.compradorTitularId)
    params.set('compradorTitularId', filtros.compradorTitularId);
  if (filtros.fechaDesde) params.set('fechaDesde', filtros.fechaDesde);
  if (filtros.fechaHasta) params.set('fechaHasta', filtros.fechaHasta);
  if (filtros.referenciaProveedor)
    params.set('referenciaProveedor', filtros.referenciaProveedor);
  if (filtros.page != null) params.set('page', String(filtros.page));
  if (filtros.pageSize != null)
    params.set('pageSize', String(filtros.pageSize));

  const query = params.toString();
  return query
    ? `/api/v1/compras/ordenes?${query}`
    : '/api/v1/compras/ordenes';
}
