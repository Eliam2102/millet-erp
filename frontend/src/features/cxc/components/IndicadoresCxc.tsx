import { Link } from '@tanstack/react-router';
import { Bell, TrendingDown } from 'lucide-react';
import { useAlertasCartera } from '@/features/cxc/api/useAlertas';
import { useAntiguedadSaldos } from '@/features/cxc/api/useCartera';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;IndicadoresCxc/&gt;</c> — indicadores rápidos del landing
 * (05-frontend-diseno §2: vencido + alertas abiertas). Gateado por
 * <c>cartera.leer</c>; el vencido se consulta POR MONEDA (los totales
 * cross-divisa del reporte no se muestran — §5 multi-moneda).
 */
export function IndicadoresCxc() {
  const puedeLeerCartera = useHasPermission(
    PermisosCanonicos.CuentasPorCobrarCarteraLeer,
  );

  const alertas = useAlertasCartera(
    { atendida: false, limit: 1 },
    { enabled: puedeLeerCartera },
  );
  const vencidoMxn = useAntiguedadSaldos(
    { moneda: 'MXN' },
    { enabled: puedeLeerCartera },
  );
  const vencidoUsd = useAntiguedadSaldos(
    { moneda: 'USD' },
    { enabled: puedeLeerCartera },
  );

  if (!puedeLeerCartera) return null;

  const alertasAbiertas = alertas.data?.total ?? null;

  return (
    <section
      className="grid grid-cols-1 gap-3 sm:grid-cols-3"
      aria-label="Indicadores de cartera"
      data-testid="indicadores-cxc"
    >
      <Link
        to="/cxc/alertas"
        className={cn(
          'rounded-md border bg-card p-4 transition-colors hover:bg-muted/40',
          alertasAbiertas != null &&
            alertasAbiertas > 0 &&
            'border-amber-300 dark:border-amber-800',
        )}
      >
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <Bell className="h-3.5 w-3.5" aria-hidden="true" />
          Alertas pendientes
        </div>
        <p className="mt-1 font-mono text-2xl font-semibold tabular-nums">
          {alertas.isLoading ? '…' : (alertasAbiertas ?? '—')}
        </p>
      </Link>

      <IndicadorVencido
        moneda="MXN"
        cargando={vencidoMxn.isLoading}
        vencido={extraerVencido(vencidoMxn.data?.totales)}
      />
      <IndicadorVencido
        moneda="USD"
        cargando={vencidoUsd.isLoading}
        vencido={extraerVencido(vencidoUsd.data?.totales)}
      />
    </section>
  );
}

function extraerVencido(
  totales: Record<string, unknown> | null | undefined,
): number | null {
  const v = totales?.vencido;
  return typeof v === 'number' ? v : null;
}

function IndicadorVencido({
  moneda,
  vencido,
  cargando,
}: {
  moneda: string;
  vencido: number | null;
  cargando: boolean;
}) {
  return (
    <Link
      to="/cxc/cartera"
      search={{ moneda } as never}
      className={cn(
        'rounded-md border bg-card p-4 transition-colors hover:bg-muted/40',
        vencido != null && vencido > 0 && 'border-red-300 dark:border-red-900',
      )}
    >
      <div className="flex items-center gap-2 text-xs text-muted-foreground">
        <TrendingDown className="h-3.5 w-3.5" aria-hidden="true" />
        Vencido {moneda}
      </div>
      <p className="mt-1 font-mono text-2xl font-semibold tabular-nums">
        {cargando ? '…' : vencido != null ? formatoMonto(vencido, moneda) : '—'}
      </p>
    </Link>
  );
}
