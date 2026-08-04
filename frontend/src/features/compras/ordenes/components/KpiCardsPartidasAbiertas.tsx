import { TrendingDown, AlertTriangle, Truck, Receipt, CreditCard } from 'lucide-react';
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
  className?: string;
}

export function KpiCardsPartidasAbiertas({
  kpis,
  isLoading,
  className,
}: KpiCardsPartidasAbiertasProps) {
  return (
    <div
      className={cn(
        'grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5',
        className,
      )}
      data-component="kpi-cards-partidas-abiertas"
      role="region"
      aria-label="Indicadores de partidas abiertas"
    >
      <Card
        label="Partidas abiertas"
        value={kpis?.countPartidasAbiertas}
        loading={isLoading}
        Icon={TrendingDown}
        tono="primary"
      />
      <Card
        label="Atrasadas"
        value={kpis?.countAtrasadas}
        loading={isLoading}
        Icon={AlertTriangle}
        tono="rojo"
      />
      <Card
        label="Recepción parcial"
        value={kpis?.countConRecepcionParcial}
        loading={isLoading}
        Icon={Truck}
        tono="ambar"
      />
      <Card
        label="Facturación parcial"
        value={kpis?.countConFacturacionParcial}
        loading={isLoading}
        Icon={Receipt}
        tono="ambar"
      />
      <Card
        label="Sin pago"
        value={kpis?.countSinPago}
        loading={isLoading}
        Icon={CreditCard}
        tono="ambar"
      />
    </div>
  );
}

const TONOS = {
  primary: 'border-primary/30 bg-primary/5 text-primary',
  ambar: 'border-amber-300 bg-amber-50 text-amber-800',
  rojo: 'border-rose-300 bg-rose-50 text-rose-800',
} as const;

function Card({
  label,
  value,
  loading,
  Icon,
  tono,
}: {
  label: string;
  value: number | undefined;
  loading?: boolean;
  Icon: React.ComponentType<{ className?: string }>;
  tono: keyof typeof TONOS;
}) {
  return (
    <div
      className={cn(
        'rounded-md border p-3 transition-colors',
        TONOS[tono],
      )}
      data-kpi={label}
    >
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium uppercase tracking-wide opacity-90">
          {label}
        </span>
        <Icon className="h-4 w-4 opacity-80" />
      </div>
      <p className="mt-1 text-2xl font-semibold tabular-nums">
        {loading ? (
          <span className="inline-block h-7 w-12 animate-pulse rounded bg-current opacity-20" />
        ) : (
          (value ?? 0).toLocaleString()
        )}
      </p>
    </div>
  );
}
