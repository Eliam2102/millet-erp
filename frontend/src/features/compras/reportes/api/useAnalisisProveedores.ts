import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { BackendReporteResponse } from '@/features/cxp/api/useReportes';

/**
 * Hooks de «Análisis de proveedores» (F1-COM-13). Mirror manual del
 * contrato `GET /api/v1/compras/reportes/analisis-proveedores/*`
 * (.local-context/contrato-86e3jvu98.md). Mismo patrón que
 * <c>features/cxp/api/useReportes.ts</c>.
 */
const BASE = '/api/v1/compras/reportes/analisis-proveedores';

export interface FiltrosAnalisis {
  desde: string;
  hasta: string;
  sucursalId?: string;
  proveedorId?: string;
  articuloId?: string;
  departamentoId?: string;
  moneda?: string;
  estado?: string;
}

export interface AnalisisResponse {
  reporte: BackendReporteResponse;
  /** Una fila por moneda; nunca un total entre monedas. */
  totalesPorMoneda: Record<string, unknown>[];
  /** Solo `/saldos`. */
  saldoInicial?: { moneda: string; ordenes: number; pendienteFisico: number }[];
  /** Solo `/cumplimiento`; se muestran verbatim. */
  formulas?: string[];
}

export type IndicadorDrill =
  | 'compras'
  | 'saldos'
  | 'a-tiempo'
  | 'completo'
  | 'incidencias'
  | 'diferencias-precio';

export type OrigenCompromiso =
  | 'incidencia'
  | 'promesa-proveedor'
  | 'linea'
  | 'oc-proveedor'
  | 'oc-esperada';

export interface OrdenDrillLinea {
  lineaId: string;
  numero: number;
  articulo: string;
  cantidad: number;
  recibidoNeto: number;
  compromiso: string | null;
  origenCompromiso: OrigenCompromiso | null;
  fechaCompletada: string | null;
  evaluable: boolean;
  aTiempo: boolean | null;
  completa: boolean | null;
}

export interface OrdenDrillFactura {
  facturaId: string;
  serie: string | null;
  folio: string | null;
  fecha: string;
  moneda: string;
  total: number;
  diferenciaContraOc: number;
  estado: string;
}

export interface OrdenDrill {
  ocId: string;
  folio: string;
  proveedorId: string;
  proveedor: string;
  moneda: string;
  fechaDocumento: string;
  estado: string;
  ordenado: number;
  porRecibir: number;
  porFacturar: number;
  porPagar: number;
  pendientes: { recepcion: boolean; facturacion: boolean; pago: boolean };
  incidencias: number;
  lineas: OrdenDrillLinea[];
  facturas: OrdenDrillFactura[];
}

export interface OrdenesDrillResponse {
  items: OrdenDrill[];
}

function buildQuery(params: object): string {
  const sp = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') sp.set(key, String(value));
  }
  return sp.toString();
}

function useAnalisis<T>(ruta: string, params: object, enabled: boolean) {
  return useQuery({
    queryKey: [...comprasKeys.all, 'reportes', 'analisis-proveedores', ruta, params] as const,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<T>(`${BASE}/${ruta}?${buildQuery(params)}`, { signal });
      return data;
    },
    enabled,
  });
}

export const useAnalisisCompras = (
  params: FiltrosAnalisis & { agruparPor: string },
  enabled = true,
) => useAnalisis<AnalisisResponse>('compras', params, enabled);

export const useAnalisisSaldos = (params: FiltrosAnalisis, enabled = true) =>
  useAnalisis<AnalisisResponse>('saldos', params, enabled);

export const useAnalisisCumplimiento = (params: FiltrosAnalisis, enabled = true) =>
  useAnalisis<AnalisisResponse>('cumplimiento', params, enabled);

export const useAnalisisOrdenes = (
  params: FiltrosAnalisis & { indicador: IndicadorDrill },
  enabled = true,
) => useAnalisis<OrdenesDrillResponse>('ordenes', params, enabled);
