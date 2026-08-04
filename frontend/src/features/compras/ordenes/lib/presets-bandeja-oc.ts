import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';
import {
  DEFAULT_BANDEJA_OC_SEARCH,
  type BandejaOcSearch,
} from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';

/**
 * Presets clickeables (chips) de la bandeja P1. Cada preset es un
 * shortcut a una combinación de filtros que el comprador o el
 * autorizador suelen consultar.
 *
 * <para><b>Restricción de scope</b>: el endpoint <c>GET /ordenes</c>
 * acepta cada filtro como un valor single (no array, no operadores
 * "≠"). Los presets que el doc 05 §10.1 plantea como compuestos
 * ("Pendientes recepción" = <c>subEstadoRecepcion ≠ Completa</c>) se
 * resuelven escogiendo el sub-estado más representativo del bucket
 * (e.g., <c>SinRecepcion</c>) — el caller puede combinar varios
 * presets ajustando el dropdown lateral.</para>
 *
 * <para><b>Sin filtros user-aware todavía</b> ("Mis borradores"
 * requiere <c>compradorTitularId = me</c>): se agregan en UF1-PR2
 * follow-up o en UF2 cuando integremos la auth-store en este flujo.
 * Para esta primera entrega el set de presets es state-based
 * solamente.</para>
 */
export interface BandejaOcPreset {
  /** Identificador estable para tests/scraping. */
  id: string;
  /** Etiqueta visible en el chip. */
  label: string;
  /** Filtros que aplica al click (reemplaza los actuales). */
  search: BandejaOcSearch;
}

export const BANDEJA_OC_PRESETS: readonly BandejaOcPreset[] = [
  {
    id: 'pendientes-n1',
    label: 'En autorización N1',
    search: {
      ...DEFAULT_BANDEJA_OC_SEARCH,
      estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
    },
  },
  {
    id: 'pendientes-n2',
    label: 'En autorización N2',
    search: {
      ...DEFAULT_BANDEJA_OC_SEARCH,
      estado: EstadoOrdenCompra.EnAutorizacionDireccion,
    },
  },
  {
    id: 'sin-recepcion',
    label: 'Autorizadas — sin recepción',
    search: {
      ...DEFAULT_BANDEJA_OC_SEARCH,
      estado: EstadoOrdenCompra.Autorizada,
      subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    },
  },
  {
    id: 'sin-factura',
    label: 'Autorizadas — sin factura',
    search: {
      ...DEFAULT_BANDEJA_OC_SEARCH,
      estado: EstadoOrdenCompra.Autorizada,
      subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    },
  },
  {
    id: 'sin-pago',
    label: 'Autorizadas — sin pago',
    search: {
      ...DEFAULT_BANDEJA_OC_SEARCH,
      estado: EstadoOrdenCompra.Autorizada,
      subEstadoPago: SubEstadoPago.SinPago,
    },
  },
  {
    id: 'cerradas',
    label: 'Cerradas',
    search: {
      ...DEFAULT_BANDEJA_OC_SEARCH,
      estado: EstadoOrdenCompra.Cerrada,
    },
  },
];

/**
 * Detecta si el search actual coincide exactamente con un preset.
 * Útil para resaltar visualmente el chip activo.
 */
export function detectarPresetActivo(
  search: BandejaOcSearch,
): BandejaOcPreset | null {
  for (const preset of BANDEJA_OC_PRESETS) {
    if (matchSearch(search, preset.search)) return preset;
  }
  return null;
}

/**
 * Igualdad campo-a-campo de los filtros que un preset declara. La
 * paginación (<c>page</c>/<c>pageSize</c>) se compara también: si el
 * usuario está en página 2, ningún preset coincide hasta que vuelva
 * a la 1 — comportamiento esperado (un preset reinicia el cursor).
 */
function matchSearch(a: BandejaOcSearch, b: BandejaOcSearch): boolean {
  return (
    a.estado === b.estado &&
    a.subEstadoRecepcion === b.subEstadoRecepcion &&
    a.subEstadoFacturacion === b.subEstadoFacturacion &&
    a.subEstadoPago === b.subEstadoPago &&
    a.proveedorId === b.proveedorId &&
    a.compradorTitularId === b.compradorTitularId &&
    a.fechaDesde === b.fechaDesde &&
    a.fechaHasta === b.fechaHasta &&
    a.q === b.q &&
    a.page === b.page &&
    a.pageSize === b.pageSize
  );
}
