/**
 * Query keys de TanStack Query para el submódulo Compras OC. Mismo
 * shape estructural que <c>features/compras/api/keys.ts</c> (RQ): el
 * primer slot es el módulo, el segundo el submódulo/recurso, el
 * tercero la acción, y siguen los filtros. Esa estructura permite
 * invalidación masiva con
 * <c>queryClient.invalidateQueries({ queryKey: ordenesKeys.all })</c>.
 *
 * <para><b>¿Por qué un namespace separado de <c>comprasKeys</c>?</b>
 * RQ y OC son agregados independientes; una mutation en OC no debería
 * obligar a invalidar la cache de RQ. Mantener namespaces separados
 * deja cada submódulo dueño de sus invalidaciones. Para invalidaciones
 * cross (p. ej. cuando consolidar OC marca RQs como
 * "comprometidas"), el caller invalida ambas familias explícitamente
 * (futuro UF2-PR2).</para>
 */

import type {
  EstadoOrdenCompra,
  SubEstadoRecepcion,
  SubEstadoFacturacion,
  SubEstadoPago,
} from '@/features/compras/ordenes/api/types';

/**
 * Filtros aceptados por <c>GET /api/v1/compras/ordenes</c>. Mirror de
 * los <c>[FromQuery]</c> del endpoint (F6-PR3 backend). Notar que la
 * paginación usa <c>page</c>/<c>pageSize</c> — distinto a RQ que usa
 * <c>offset</c>/<c>limit</c>.
 */
export interface ListarOrdenesCompraFiltros {
  estado?: EstadoOrdenCompra;
  subEstadoRecepcion?: SubEstadoRecepcion;
  /**
   * Si <c>true</c>, solo OCs con recepción pendiente (SubEstadoRecepcion
   * != Completa). Lo usa el selector de Nueva recepción para no listar
   * OCs ya recibidas al 100%.
   */
  soloConPendienteRecepcion?: boolean;
  subEstadoFacturacion?: SubEstadoFacturacion;
  subEstadoPago?: SubEstadoPago;
  proveedorId?: string;
  compradorTitularId?: string;
  /** ISO 8601. */
  fechaDesde?: string;
  /** ISO 8601. */
  fechaHasta?: string;
  /** Texto libre para buscar por referencia del proveedor (no por folio). */
  referenciaProveedor?: string;
  /** Default backend = 1. */
  page?: number;
  /** Default backend = 50. Máx 200. */
  pageSize?: number;
}

export const ordenesKeys = {
  all: ['compras', 'ordenes'] as const,

  list: (filtros: ListarOrdenesCompraFiltros) =>
    [...ordenesKeys.all, 'list', filtros] as const,

  detail: (id: string) => [...ordenesKeys.all, 'detail', id] as const,

  /** UF4-PR1: bandeja P2 de pendientes-autorización filtrada por nivel. */
  pendientes: (nivel: 'Nivel1' | 'Nivel2' | 'todos') =>
    [...ordenesKeys.all, 'pendientes', nivel] as const,
} as const;
