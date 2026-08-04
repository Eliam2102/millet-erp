import { hoyLocalISO } from '@/lib/datetime';
import { Controller, useForm } from 'react-hook-form';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { SucursalSelector, ReporteShell } from '@/components/erp';
import {
  useLiquidacionCaja,
  type ReporteFila,
} from '@/features/facturacion/api/useReportes';
import { esApiError } from '@/lib/api';
import type { LiquidacionCajaSearch } from '@/features/facturacion/lib/reportes-search-schema';

const FROM = '/_app/facturacion/reportes/liquidacion-caja' as const;

function primerDiaDeMes(): string {
  const ahora = new Date();
  return new Date(ahora.getFullYear(), ahora.getMonth(), 1)
    .toISOString()
    .slice(0, 10);
}
function hoy(): string {
  return hoyLocalISO();
}

/**
 * <c>Reporte de Liquidación de caja</c> (FE-F9). Facturado por forma de
 * pago en un rango de fechas, vía <c>&lt;ReporteShell&gt;</c> (ADR-0036).
 */
export function ReporteLiquidacionCaja() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const desde = search.desde ?? primerDiaDeMes();
  const hasta = search.hasta ?? hoy();

  // Form solo para el SucursalSelector (Controller); fechas van por search.
  const form = useForm<{ sucursalId: string }>({
    defaultValues: { sucursalId: search.sucursalId ?? '' },
  });

  const query = useLiquidacionCaja({
    sucursalId: search.sucursalId,
    desde,
    hasta,
  });

  function actualizar(parcial: Partial<LiquidacionCajaSearch>) {
    navigate({
      to: '/facturacion/reportes/liquidacion-caja',
      search: { ...search, ...parcial },
    });
  }

  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Sucursal</Label>
        <div className="w-64">
          <Controller
            name="sucursalId"
            control={form.control}
            render={({ field }) => (
              <SucursalSelector
                value={field.value || null}
                onChange={(id) => {
                  field.onChange(id ?? '');
                  actualizar({ sucursalId: id ?? undefined });
                }}
              />
            )}
          />
        </div>
      </div>
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Desde</Label>
        <Input
          type="date"
          value={desde}
          onChange={(e) => actualizar({ desde: e.target.value })}
        />
      </div>
      <div className="space-y-1">
        <Label className="text-xs text-muted-foreground">Hasta</Label>
        <Input
          type="date"
          value={hasta}
          onChange={(e) => actualizar({ hasta: e.target.value })}
        />
      </div>
    </div>
  );

  return (
    <div className="space-y-4 px-4 py-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">
          Liquidación de caja
        </h1>
        <p className="text-sm text-muted-foreground">
          Facturado por forma de pago en el rango seleccionado.
        </p>
      </header>

      <ReporteShell<ReporteFila>
        reporte={query.data}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={filtrosUi}
        nombreArchivo="liquidacion-caja"
      />
    </div>
  );
}
