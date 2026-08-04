import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';

/**
 * Item del reporte P9 — Partidas abiertas (UF7-PR1).
 * Mirror de <c>PartidaAbiertaResumen</c> backend (F7-PR1).
 */
export interface PartidaAbiertaResumen {
  id: string;
  folio: string;
  folioAnio: number;
  estado: EstadoOrdenCompra;
  subEstadoRecepcion: SubEstadoRecepcion;
  subEstadoFacturacion: SubEstadoFacturacion;
  subEstadoPago: SubEstadoPago;
  proveedorId: string;
  compradorTitularId: string;
  moneda: string;
  /** ISO 8601 UTC. */
  fechaDocumento: string;
  /** ISO 8601 UTC, opcional. */
  fechaEntregaEsperada: string | null;
  /** Computed por el backend contra fechaEntregaEsperada (>0 = atrasada). */
  diasAtrasados: number;
  referenciaProveedor: string | null;
  numeroContenedor: string | null;
  codigoRuta: string | null;
  semanaEmbarque: string | null;
}

export interface ListarPartidasAbiertasResponse {
  items: PartidaAbiertaResumen[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PartidasAbiertasFiltros {
  estado?: EstadoOrdenCompra;
  subEstadoRecepcion?: SubEstadoRecepcion;
  subEstadoFacturacion?: SubEstadoFacturacion;
  subEstadoPago?: SubEstadoPago;
  proveedorId?: string;
  compradorTitularId?: string;
  fechaDesde?: string;
  fechaHasta?: string;
  numeroContenedor?: string;
  codigoRuta?: string;
  semanaEmbarque?: string;
  diasAtrasadosMinimos?: number;
  page?: number;
  pageSize?: number;
}

/**
 * <c>usePartidasAbiertas(filtros)</c> — wrapper del endpoint
 * <c>GET /api/v1/compras/ordenes/partidas-abiertas</c>. Permiso
 * requerido: <c>compras.ordenes.reportes-partidas-abiertas</c>.
 *
 * <para>Performance target del doc UF7-PR1: P95 query < 500ms con 5k
 * OCs activas seed. El backend está indexado por
 * <c>ix_oc_partidas_abiertas</c>.</para>
 */
export function usePartidasAbiertas(filtros: PartidasAbiertasFiltros = {}) {
  return useQuery({
    queryKey: [...ordenesKeys.all, 'partidas-abiertas', filtros] as const,
    queryFn: async ({ signal }) => {
      const path = buildPath(filtros);
      const { data } = await apiRequest<ListarPartidasAbiertasResponse>(path, {
        signal,
      });
      return data;
    },
    // Mantén el cache en navegaciones rápidas dentro del reporte; los
    // filtros distintos generan otra cache key.
    staleTime: 30_000,
  });
}

function buildPath(filtros: PartidasAbiertasFiltros): string {
  const params = new URLSearchParams();
  if (filtros.estado != null) params.set('estado', String(filtros.estado));
  if (filtros.subEstadoRecepcion != null)
    params.set('subEstadoRecepcion', String(filtros.subEstadoRecepcion));
  if (filtros.subEstadoFacturacion != null)
    params.set('subEstadoFacturacion', String(filtros.subEstadoFacturacion));
  if (filtros.subEstadoPago != null)
    params.set('subEstadoPago', String(filtros.subEstadoPago));
  if (filtros.proveedorId) params.set('proveedorId', filtros.proveedorId);
  if (filtros.compradorTitularId)
    params.set('compradorTitularId', filtros.compradorTitularId);
  if (filtros.fechaDesde) params.set('fechaDesde', filtros.fechaDesde);
  if (filtros.fechaHasta) params.set('fechaHasta', filtros.fechaHasta);
  if (filtros.numeroContenedor)
    params.set('numeroContenedor', filtros.numeroContenedor);
  if (filtros.codigoRuta) params.set('codigoRuta', filtros.codigoRuta);
  if (filtros.semanaEmbarque)
    params.set('semanaEmbarque', filtros.semanaEmbarque);
  if (filtros.diasAtrasadosMinimos != null)
    params.set('diasAtrasadosMinimos', String(filtros.diasAtrasadosMinimos));
  if (filtros.page != null) params.set('page', String(filtros.page));
  if (filtros.pageSize != null)
    params.set('pageSize', String(filtros.pageSize));
  const qs = params.toString();
  return qs
    ? `/api/v1/compras/ordenes/partidas-abiertas?${qs}`
    : '/api/v1/compras/ordenes/partidas-abiertas';
}
