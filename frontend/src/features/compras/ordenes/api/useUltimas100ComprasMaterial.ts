import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * Mirror del DTO backend <c>CompraMaterialResumen</c> (UF7-PR3,
 * F7-PR3 §8.5).
 */
export interface CompraMaterialResumen {
  lineaId: string;
  ordenCompraId: string;
  folioOc: string;
  proveedorId: string;
  cantidad: number;
  unidadMedida: string;
  precioUnitario: number;
  moneda: string;
  /** ISO 8601 UTC. */
  fechaDocumento: string;
}

export interface ListarUltimas100ComprasResponse {
  items: CompraMaterialResumen[];
}

export interface UltimasComprasFiltros {
  proveedorId?: string;
  fechaDesde?: string;
  cantidadMinima?: number;
}

/**
 * <c>useUltimas100ComprasMaterial(articuloId, filtros)</c> — wrapper
 * del endpoint <c>GET /api/v1/compras/articulos/{id}/historial-compras</c>
 * (UF7-PR3, P11). Permite al comprador ver tendencia de precios y
 * proveedores para un material específico (cap 100 líneas, ordenado
 * por fecha doc DESC).
 *
 * <para>Permiso: <c>compras.ordenes.leer</c>. Cache 5 min — los datos
 * cambian al autorizar nuevas OCs con líneas del artículo.</para>
 */
export function useUltimas100ComprasMaterial(
  articuloId: string,
  filtros: UltimasComprasFiltros = {},
) {
  return useQuery({
    queryKey: [
      'compras',
      'articulos',
      articuloId,
      'historial-compras',
      filtros,
    ] as const,
    enabled: !!articuloId,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.proveedorId) params.set('proveedorId', filtros.proveedorId);
      if (filtros.fechaDesde) params.set('fechaDesde', filtros.fechaDesde);
      if (filtros.cantidadMinima != null)
        params.set('cantidadMinima', String(filtros.cantidadMinima));
      const qs = params.toString();
      const path = qs
        ? `/api/v1/compras/articulos/${articuloId}/historial-compras?${qs}`
        : `/api/v1/compras/articulos/${articuloId}/historial-compras`;
      const { data } = await apiRequest<ListarUltimas100ComprasResponse>(path, {
        signal,
      });
      return data;
    },
  });
}
