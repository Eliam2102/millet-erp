import { SucursalSelector } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useLiquidacionCaja, type ReporteFila } from '@/features/facturacion/api/useReportes';
import type { LiquidacionCajaSearch } from '@/features/facturacion/lib/reportes-search-schema';
import { esApiError } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Controller, useForm } from 'react-hook-form';
import { ReporteFacturacion } from '../components/ReporteFacturacion';

const FROM = '/_app/facturacion/reportes/liquidacion-caja' as const;

function primerDiaDeMes(): string {
  const ahora = new Date();
  return new Date(ahora.getFullYear(), ahora.getMonth(), 1).toISOString().slice(0, 10);
}
function hoy(): string {
  return hoyLocalISO();
}

/**
 * <c>Reporte de Liquidación de caja</c> (FE-F9). Facturado por forma de
 * pago en un rango de fechas, vía <c>&lt;ReporteFacturacion&gt;</c> (ADR-0036).
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
        <Label className="text-xs text-ink-muted">Sucursal</Label>
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
        <Label className="text-xs text-ink-muted">Desde</Label>
        <Input type="date" value={desde} onChange={(e) => actualizar({ desde: e.target.value })} />
      </div>
      <div className="space-y-1">
        <Label className="text-xs text-ink-muted">Hasta</Label>
        <Input type="date" value={hasta} onChange={(e) => actualizar({ hasta: e.target.value })} />
      </div>
    </div>
  );

  return (
    <div className="space-y-5">
      <div>
        <PageHeader
          titulo={<>Liquidación de caja</>}
          descripcion={
            <>
              Sesiones cerradas por caja y cajero en el rango seleccionado; abre el corte de cada
              una.
            </>
          }
        />
      </div>

      <ReporteFacturacion<ReporteFila>
        reporte={query.data}
        isLoading={query.isLoading}
        error={esApiError(query.error) ? query.error : undefined}
        onRetry={() => query.refetch()}
        filtrosUi={filtrosUi}
        nombreArchivo="liquidacion-caja"
        etiquetaAccion="Corte de caja"
        accionFila={(fila) =>
          typeof fila.sesionId === 'string' ? (
            <Link
              to="/facturacion/caja/corte/$sesionId"
              params={{ sesionId: fila.sesionId }}
              className="text-sm font-medium text-brand hover:underline"
            >
              Ver corte
            </Link>
          ) : null
        }
      />
    </div>
  );
}
