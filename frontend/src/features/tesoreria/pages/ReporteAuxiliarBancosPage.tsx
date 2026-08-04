import { hoyLocalISO } from '@/lib/datetime';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Landmark } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { EmptyState, ReporteShell } from '@/components/erp';
import { useReporteAuxiliarBancos } from '@/features/tesoreria/api/useTesoreria';
import { CuentaBancariaSelector } from '@/features/tesoreria/components/CuentaBancariaSelector';
import { adaptarReporteTesoreria } from '@/features/tesoreria/lib/reporte-tesoreria-adapter';
import { esApiError } from '@/lib/api';

const FROM = '/_app/tesoreria/reportes/auxiliar-bancos' as const;

function inicioDeMes(): string {
  const hoy = new Date();
  return `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}-01`;
}

function hoyIso(): string {
  return hoyLocalISO();
}

/**
 * <c>Auxiliar de bancos</c> (TES-FE-PR6, ADR-0036 / TES-6): libro
 * cronológico por cuenta y período con saldo acumulado. La cuenta es
 * obligatoria — sin ella la query no se dispara.
 */
export function ReporteAuxiliarBancosPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const desde = search.desde ?? inicioDeMes();
  const hasta = search.hasta ?? hoyIso();
  const cuentaId = search.cuentaBancariaId ?? null;

  const query = useReporteAuxiliarBancos({
    cuentaBancariaId: cuentaId,
    desde,
    hasta,
  });

  function actualizar(parcial: Record<string, string | undefined>) {
    navigate({
      to: '/tesoreria/reportes/auxiliar-bancos',
      search: { ...search, ...parcial },
    });
  }

  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="w-72 space-y-1">
        <Label className="text-xs text-muted-foreground">Cuenta</Label>
        <CuentaBancariaSelector
          value={cuentaId}
          onChange={(id) => actualizar({ cuentaBancariaId: id ?? undefined })}
        />
      </div>
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
    </div>
  );

  return (
    <div className="space-y-4 px-4 py-6">
      <header data-print="hidden">
        <h1 className="text-2xl font-semibold tracking-tight">
          Auxiliar de bancos
        </h1>
        <p className="text-sm text-muted-foreground">
          Libro cronológico por cuenta con saldo acumulado (el saldo inicial
          bancario real llega con la conciliación).
        </p>
      </header>

      {cuentaId == null ? (
        <div className="space-y-4">
          {filtrosUi}
          <EmptyState
            icon={<Landmark className="h-10 w-10" />}
            title="Selecciona una cuenta bancaria."
            description="El auxiliar es por cuenta — elige una para generar el reporte."
          />
        </div>
      ) : (
        <ReporteShell
          reporte={adaptarReporteTesoreria(query.data)}
          isLoading={query.isLoading}
          error={esApiError(query.error) ? query.error : undefined}
          onRetry={() => query.refetch()}
          filtrosUi={filtrosUi}
          nombreArchivo="auxiliar-bancos-tesoreria"
        />
      )}
    </div>
  );
}
