import { hoyLocalISO } from '@/lib/datetime';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ReporteShell } from '@/components/erp';
import { useReporteFlujoEfectivo } from '@/features/tesoreria/api/useTesoreria';
import { CuentaBancariaSelector } from '@/features/tesoreria/components/CuentaBancariaSelector';
import { adaptarReporteTesoreria } from '@/features/tesoreria/lib/reporte-tesoreria-adapter';
import { esApiError } from '@/lib/api';

const FROM = '/_app/tesoreria/reportes/flujo-efectivo' as const;

function inicioDeMes(): string {
  const hoy = new Date();
  return `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}-01`;
}

function hoyIso(): string {
  return hoyLocalISO();
}

/**
 * <c>Reporte de flujo de efectivo</c> (TES-FE-PR6, ADR-0036 / TES-6):
 * ingresos y egresos clasificados por concepto sobre
 * <c>&lt;ReporteShell&gt;</c> con export PDF/Excel client-side.
 */
export function ReporteFlujoEfectivoPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const desde = search.desde ?? inicioDeMes();
  const hasta = search.hasta ?? hoyIso();

  const query = useReporteFlujoEfectivo({
    desde,
    hasta,
    cuentaBancariaId: search.cuentaBancariaId,
    moneda: search.moneda,
  });

  function actualizar(parcial: Record<string, string | undefined>) {
    navigate({
      to: '/tesoreria/reportes/flujo-efectivo',
      search: { ...search, ...parcial },
    });
  }

  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Del</Label>
        <Input
          type="date"
          className="w-40"
          value={desde}
          onChange={(e) => actualizar({ desde: e.target.value || undefined })}
        />
      </div>
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Al</Label>
        <Input
          type="date"
          className="w-40"
          value={hasta}
          onChange={(e) => actualizar({ hasta: e.target.value || undefined })}
        />
      </div>
      <div className="w-72 space-y-1">
        <Label className="text-xs text-muted-foreground">Cuenta (opcional)</Label>
        <CuentaBancariaSelector
          value={search.cuentaBancariaId ?? null}
          onChange={(id) => actualizar({ cuentaBancariaId: id ?? undefined })}
        />
      </div>
    </div>
  );

  return (
    <div className="space-y-4 px-4 py-6">
      <header data-print="hidden">
        <h1 className="text-2xl font-semibold tracking-tight">
          Flujo de efectivo
        </h1>
        <p className="text-sm text-muted-foreground">
          Ingresos y egresos por concepto (Operación / Inversión /
          Financiamiento). Catálogo de conceptos provisional.
        </p>
      </header>

      <ReporteShell
        reporte={adaptarReporteTesoreria(query.data)}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={filtrosUi}
        nombreArchivo="flujo-efectivo-tesoreria"
      />
    </div>
  );
}
