import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * Mirror de <c>KpisPartidasAbiertasResponse</c> backend (F7-PR3).
 * 5 contadores agregados sobre las mismas OCs que el reporte
 * <c>partidas-abiertas</c>.
 */
export interface KpisPartidasAbiertasResponse {
  countPartidasAbiertas: number;
  countAtrasadas: number;
  countConRecepcionParcial: number;
  countConFacturacionParcial: number;
  countSinPago: number;
}

/**
 * Filtros aceptados por el endpoint de KPIs (subset del reporte;
 * solo proveedor + comprador + rango de fechas, NO los sub-estados).
 */
export interface KpisPartidasAbiertasFiltros {
  proveedorId?: string;
  compradorTitularId?: string;
  fechaDesde?: string;
  fechaHasta?: string;
}

/**
 * <c>useKpisPartidasAbiertas(filtros)</c> — wrapper del endpoint
 * <c>GET /partidas-abiertas/kpis</c>. Consume los KPIs reactivos a
 * los filtros aplicados — el componente
 * <c>&lt;KpiCardsPartidasAbiertas/&gt;</c> los renderea como 5 cards.
 *
 * <para>Permiso: <c>compras.ordenes.reportes-partidas-abiertas</c>
 * (mismo que el reporte). El backend NO acepta filtros de sub-estado
 * en este endpoint — los KPIs son sobre TODA la población de partidas
 * abiertas (filtrada por proveedor/comprador/fechas).</para>
 */
export function useKpisPartidasAbiertas(
  filtros: KpisPartidasAbiertasFiltros = {},
) {
  return useQuery({
    queryKey: [
      ...ordenesKeys.all,
      'partidas-abiertas',
      'kpis',
      filtros,
    ] as const,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.proveedorId) params.set('proveedorId', filtros.proveedorId);
      if (filtros.compradorTitularId)
        params.set('compradorTitularId', filtros.compradorTitularId);
      if (filtros.fechaDesde) params.set('fechaDesde', filtros.fechaDesde);
      if (filtros.fechaHasta) params.set('fechaHasta', filtros.fechaHasta);
      const qs = params.toString();
      const path = qs
        ? `/api/v1/compras/ordenes/partidas-abiertas/kpis?${qs}`
        : '/api/v1/compras/ordenes/partidas-abiertas/kpis';
      const { data } = await apiRequest<KpisPartidasAbiertasResponse>(path, {
        signal,
      });
      return data;
    },
    staleTime: 30_000,
  });
}
