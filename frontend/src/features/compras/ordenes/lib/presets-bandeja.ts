import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';
import type { BandejaOcSearch } from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';

/**
 * Presets de la bandeja P1 de OCs (UF6-PR1). Cada preset es un par
 * <c>(label, search)</c> donde <c>search</c> es un subset de
 * <c>BandejaOcSearch</c> que el caller mergea con el default antes de
 * navegar (<c>navigate({ to, search: { ...DEFAULT_BANDEJA_OC_SEARCH, ...preset.search }})</c>).
 *
 * <para>Los presets resetean siempre <c>page=1</c> y <c>pageSize=50</c>
 * (defaults) — son atajos de "vista nueva", no "página 5 con filtros".</para>
 *
 * <para><b>Diferido</b>: el preset "Mis duplicadas" del doc UF6-PR1
 * requiere un filtro <c>ocOrigenId != null AND compradorTitular = me</c>
 * que el endpoint actual no soporta. Va en un futuro PR cuando el
 * backend agregue los filtros (<c>soloDuplicadas: bool</c> +
 * <c>compradorEs Yo: bool</c> o equivalente).</para>
 */
export interface BandejaPreset {
  /** Identificador estable (slug) — útil para tests/analytics. */
  id: string;
  /** Texto humano del menú. */
  label: string;
  /** Tooltip explicando qué muestra. */
  descripcion: string;
  /** Subset de search params a aplicar. Sin <c>page</c>/<c>pageSize</c>. */
  search: Partial<Omit<BandejaOcSearch, 'page' | 'pageSize'>>;
}

export const BANDEJA_OC_PRESETS: readonly BandejaPreset[] = [
  {
    id: 'autorizadas-pendientes-recepcion',
    label: 'Autorizadas pendientes de recepción',
    descripcion:
      'OCs autorizadas que aún no inician recepción. Útil para seguimiento de proveedores.',
    search: {
      estado: EstadoOrdenCompra.Autorizada,
      subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    },
  },
  {
    id: 'autorizadas-recepcion-parcial',
    label: 'Autorizadas con recepción parcial',
    descripcion:
      'OCs en proceso de recepción (algo entró, falta el resto).',
    search: {
      estado: EstadoOrdenCompra.Autorizada,
      subEstadoRecepcion: SubEstadoRecepcion.Parcial,
    },
  },
  {
    id: 'recibidas-pendientes-factura',
    label: 'Recibidas pendientes de factura',
    descripcion:
      'OCs con recepción completa que aún no tienen factura del proveedor.',
    search: {
      subEstadoRecepcion: SubEstadoRecepcion.Completa,
      subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    },
  },
  {
    id: 'facturadas-pendientes-pago',
    label: 'Facturadas pendientes de pago',
    descripcion:
      'OCs con factura registrada pendientes de programación o pago.',
    search: {
      subEstadoFacturacion: SubEstadoFacturacion.Completa,
      subEstadoPago: SubEstadoPago.SinPago,
    },
  },
  {
    id: 'canceladas',
    label: 'Canceladas (auditoría)',
    descripcion:
      'OCs en estado Cancelada para auditoría y revisión histórica.',
    search: {
      estado: EstadoOrdenCompra.Cancelada,
    },
  },
  {
    id: 'rechazadas',
    label: 'Rechazadas (auditoría)',
    descripcion:
      'OCs rechazadas en autorización; siguen editables para re-transmitir.',
    search: {
      estado: EstadoOrdenCompra.Rechazada,
    },
  },
] as const;
