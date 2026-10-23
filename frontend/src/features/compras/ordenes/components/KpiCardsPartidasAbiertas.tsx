import type { PartidasAbiertasSearch } from '../lib/partidas-abiertas-search-schema';
import { SubEstadoRecepcion, SubEstadoFacturacion, SubEstadoPago } from '../api/types';
import { KpiCard } from '@/components/erp/display/KpiCard';
import type { KpisPartidasAbiertasResponse } from '@/features/compras/ordenes/api/useKpisPartidasAbiertas';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;KpiCardsPartidasAbiertas/&gt;</c> — 5 cards arriba del
 * reporte P9 con los counts de KPIs (UF7-PR1, FOC10).
 *
 * <para>El backend devuelve solo counts (no montos en F7-PR3 actual);
 * cuando el endpoint promueva a sumas de <c>total_a_pagar</c> /
 * <c>monto_pendiente</c>, los labels y formato se ajustan sin cambiar
 * la API del componente.</para>
 *
 * <para>Reactivas a los filtros de la pantalla P9. Loading state
 * skeleton fino para no parpadear cards al cambiar filtros.</para>
 */
export interface KpiCardsPartidasAbiertasProps {
  kpis: KpisPartidasAbiertasResponse | undefined;
  isLoading?: boolean;
  search?: PartidasAbiertasSearch;
  className?: string;
}

export function KpiCardsPartidasAbiertas({
  kpis,
  isLoading,
  className,
  search,
}: KpiCardsPartidasAbiertasProps) {
  function destino(filtros: Partial<PartidasAbiertasSearch> = {}) {
    const params = new URLSearchParams();
    Object.entries({
      proveedorId: search?.proveedorId,
      compradorTitularId: search?.compradorTitularId,
      fechaDesde: search?.fechaDesde,
      fechaHasta: search?.fechaHasta,
      pageSize: search?.pageSize,
      ...filtros,
      page: 1,
    }).forEach(([key, value]) => {
      if (value !== undefined && value !== null) params.set(key, String(value));
    });
    return `/compras/ordenes/partidas-abiertas?${params.toString()}`;
  }
  return (
    <div
      className={cn('grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5', className)}
      data-component="kpi-cards-partidas-abiertas"
      role="region"
      aria-label="Indicadores de partidas abiertas"
    >
      <KpiCard
        etiqueta="Partidas abiertas"
        href={destino({})}
        valor={isLoading ? '…' : (kpis?.countPartidasAbiertas?.toLocaleString('es-MX') ?? '—')}
        tono="info"
      />
      <KpiCard
        etiqueta="Atrasadas"
        href={destino({ diasAtrasadosMinimos: 1 })}
        valor={isLoading ? '…' : (kpis?.countAtrasadas?.toLocaleString('es-MX') ?? '—')}
        tono="danger"
      />
      <KpiCard
        etiqueta="Recepción parcial"
        href={destino({ subEstadoRecepcion: SubEstadoRecepcion.Parcial })}
        valor={isLoading ? '…' : (kpis?.countConRecepcionParcial?.toLocaleString('es-MX') ?? '—')}
        tono="warning"
      />
      <KpiCard
        etiqueta="Facturación parcial"
        href={destino({ subEstadoFacturacion: SubEstadoFacturacion.Parcial })}
        valor={isLoading ? '…' : (kpis?.countConFacturacionParcial?.toLocaleString('es-MX') ?? '—')}
        tono="warning"
      />
      <KpiCard
        etiqueta="Sin pago"
        href={destino({ subEstadoPago: SubEstadoPago.SinPago })}
        valor={isLoading ? '…' : (kpis?.countSinPago?.toLocaleString('es-MX') ?? '—')}
        tono="warning"
      />
    </div>
  );
}
