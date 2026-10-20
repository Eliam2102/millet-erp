import { KpiCard } from '@/components/erp';
import { useAlertasCartera } from '@/features/cxc/api/useAlertas';
import { useAntiguedadSaldos } from '@/features/cxc/api/useCartera';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * <c>&lt;IndicadoresCxc/&gt;</c> — indicadores rápidos del landing
 * (05-frontend-diseno §2: vencido + alertas abiertas). Gateado por
 * <c>cartera.leer</c>; el vencido se consulta POR MONEDA (los totales
 * cross-divisa del reporte no se muestran — §5 multi-moneda).
 */
export function IndicadoresCxc() {
  const puedeLeerCartera = useHasPermission(PermisosCanonicos.CuentasPorCobrarCarteraLeer);

  const alertas = useAlertasCartera({ atendida: false, limit: 1 }, { enabled: puedeLeerCartera });
  const vencidoMxn = useAntiguedadSaldos({ moneda: 'MXN' }, { enabled: puedeLeerCartera });
  const vencidoUsd = useAntiguedadSaldos({ moneda: 'USD' }, { enabled: puedeLeerCartera });

  if (!puedeLeerCartera) return null;

  const alertasAbiertas = alertas.data?.total ?? null;

  return (
    <section
      className="grid grid-cols-1 gap-3 sm:grid-cols-3"
      aria-label="Indicadores de cartera"
      data-testid="indicadores-cxc"
    >
      <KpiCard
        etiqueta="Alertas pendientes"
        valor={alertas.isLoading ? '…' : (alertasAbiertas ?? '—')}
        tono="warning"
        href="/cxc/alertas"
      />

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

function extraerVencido(totales: Record<string, unknown> | null | undefined): number | null {
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
    <KpiCard
      etiqueta={`Vencido ${moneda}`}
      valor={cargando ? '…' : vencido != null ? formatoMonto(vencido, moneda) : '—'}
      tono="danger"
      href={`/cxc/cartera?moneda=${moneda}`}
    />
  );
}
