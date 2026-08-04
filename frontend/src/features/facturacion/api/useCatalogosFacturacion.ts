import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  CanalVentaLookupItem,
  ClienteLookupItem,
  ProductoAwLookupItem,
} from '@/features/facturacion/api/types';

/**
 * Lookups de catálogo para los pickers del form de emisión (FAC-UX-PR4,
 * cierra <ClienteSelector>/<ProductoSelector>). Viven en Facturación —
 * los endpoints autorizan con `facturacion.facturas.emitir`, no con
 * permisos de admin de Datos Maestros. Filtros excluyentes ADR-0045
 * (el backend da precedencia a rfc/referencia).
 */
const BASE = '/api/v1/facturacion/catalogos';

export interface ClientesLookupFiltros {
  rfc?: string;
  razonSocial?: string;
  limit?: number;
}

export function useClientesLookup(filtros: ClientesLookupFiltros, opts?: { enabled?: boolean }) {
  return useQuery<ClienteLookupItem[]>({
    queryKey: facturacionKeys.clientesLookup(filtros as Record<string, unknown>),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.rfc) params.set('rfc', filtros.rfc);
      if (filtros.razonSocial) params.set('razonSocial', filtros.razonSocial);
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<ClienteLookupItem[]>(
        qs ? `${BASE}/clientes?${qs}` : `${BASE}/clientes`,
        { signal },
      );
      return data;
    },
    enabled: opts?.enabled ?? true,
    staleTime: 60_000,
  });
}

/**
 * Canales de venta activos para los selectores de captura de pedido y
 * emisión (FAC-ING-PR3). Catálogo corto y estable — sin filtros; el
 * backend devuelve solo activos ordenados por id.
 */
export function useCanalesVenta(opts?: { enabled?: boolean }) {
  return useQuery<CanalVentaLookupItem[]>({
    queryKey: facturacionKeys.canalesVentaLookup(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CanalVentaLookupItem[]>(
        `${BASE}/canales-venta`,
        { signal },
      );
      return data;
    },
    enabled: opts?.enabled ?? true,
    staleTime: 60_000,
  });
}

export interface ProductosAwLookupFiltros {
  referencia?: string;
  descripcion?: string;
  limit?: number;
}

export function useProductosAwLookup(
  filtros: ProductosAwLookupFiltros,
  opts?: { enabled?: boolean },
) {
  return useQuery<ProductoAwLookupItem[]>({
    queryKey: facturacionKeys.productosAwLookup(filtros as Record<string, unknown>),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.referencia) params.set('referencia', filtros.referencia);
      if (filtros.descripcion) params.set('descripcion', filtros.descripcion);
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<ProductoAwLookupItem[]>(
        qs ? `${BASE}/productos-aw?${qs}` : `${BASE}/productos-aw`,
        { signal },
      );
      return data;
    },
    enabled: opts?.enabled ?? true,
    staleTime: 60_000,
  });
}
