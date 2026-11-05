import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, ReporteShell, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import {
  useEjecutarRevaluacionCierre,
  useReporteRevaluacionCierre,
  useRevaluacionesCierre,
  type RevaluacionCierreResumen,
} from '@/features/cxp/api/useRevaluacionCierre';
import { backendToShellShape } from '@/features/cxp/lib/reportes-adapter';
import { esApiError } from '@/lib/api';
import { useFormIdempotencyKey } from '@/lib/api/idempotency';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { format } from 'date-fns';
import { hoyLocalISO, parseDateOnlyLocal } from '@/lib/datetime';

/** Fecha de calendario del API (`YYYY-MM-DD`) como dd/MM/yyyy, sin corrimiento de zona. */
const fecha = (iso: string) => format(parseDateOnlyLocal(iso), 'dd/MM/yyyy');

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

/** Saldos con 2 decimales y tipos de cambio con 4 (pantalla, PDF y Excel). */
function conDecimales(shape: ReturnType<typeof backendToShellShape>) {
  const decimales: Record<string, number> = { saldo: 2, tc_factura: 4, tc_cierre: 4 };
  return { ...shape, columnas: shape.columnas.map((c) => ({ ...c, decimales: decimales[c.clave] ?? null })) };
}

/** Mes anterior al de hoy: el que normalmente se cierra. */
function mesPorCerrar(): { anio: number; mes: number } {
  const [anio, mes] = hoyLocalISO().split('-').map(Number);
  return mes === 1 ? { anio: anio - 1, mes: 12 } : { anio, mes: mes - 1 };
}

/**
 * CXP-12 · Revaluación de cierre. Revalúa los saldos por pagar en moneda extranjera con el TC del último día
 * del mes (diferencia no realizada a resultados; la reversa sale sola el día 1) y muestra el reporte por
 * proveedor y documento.
 */
export function RevaluacionCierrePage() {
  const puedeEjecutar = useHasPermission(PermisosCanonicos.CuentasPorPagarRevaluacionEjecutar);
  const inicial = mesPorCerrar();
  const [anio, setAnio] = useState(String(inicial.anio));
  const [mes, setMes] = useState(String(inicial.mes));
  const [seleccion, setSeleccion] = useState<{ anio: number; mes: number } | null>(null);
  const [resumen, setResumen] = useState<RevaluacionCierreResumen | null>(null);
  const idempotencyKey = useFormIdempotencyKey();

  const lista = useRevaluacionesCierre();
  const reporte = useReporteRevaluacionCierre(seleccion);
  const ejecutar = useEjecutarRevaluacionCierre();

  const anioNumero = Number(anio);
  const anioValido = Number.isInteger(anioNumero) && anioNumero >= 2000 && anioNumero <= 2100;

  function correr() {
    ejecutar.mutate(
      { anio: anioNumero, mes: Number(mes), idempotencyKey },
      {
        onSuccess: (r) => {
          setResumen(r);
          setSeleccion({ anio: r.anio, mes: r.mes });
          toast.success('Revaluación registrada');
        },
        onError: (error) => {
          setResumen(null);
          toast.error(
            esApiError(error)
              ? (error.problem.detail ?? error.problem.title)
              : 'No se pudo correr la revaluación.',
          );
        },
      },
    );
  }

  const items = lista.data ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Revaluación de cierre"
        descripcion="Revalúa al cierre de mes los saldos por pagar en moneda extranjera con el tipo de cambio del último día; la reversa se registra sola el día 1."
      />

      {puedeEjecutar && (
        <section aria-labelledby="correr-revaluacion" className="space-y-3 rounded-md border border-line bg-surface p-4">
          <h2 id="correr-revaluacion" className="text-base font-semibold text-ink">
            Correr la revaluación
          </h2>
          <div className="flex flex-wrap items-end gap-3">
            <div className="space-y-1">
              <Label htmlFor="revaluacion-mes" className="text-xs">Mes</Label>
              <Select value={mes} onValueChange={setMes}>
                <SelectTrigger id="revaluacion-mes" className="w-44">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {MESES.map((nombre, i) => (
                    <SelectItem key={nombre} value={String(i + 1)}>{nombre}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="revaluacion-anio" className="text-xs">Año</Label>
              <Input
                id="revaluacion-anio"
                type="number"
                min={2000}
                max={2100}
                value={anio}
                onChange={(e) => setAnio(e.target.value)}
                className="w-28"
              />
            </div>
            <Button onClick={correr} disabled={!anioValido || ejecutar.isPending}>
              {ejecutar.isPending ? 'Revaluando…' : 'Correr revaluación'}
            </Button>
          </div>
          <p className="text-xs text-ink-muted">
            Solo entran las facturas con saldo abierto al último día del mes. Las pagadas antes del cierre no se revalúan.
          </p>
          {resumen && (
            <div role="status" className="space-y-1 rounded-md bg-success-bg p-3 text-sm text-success-fg">
              <p>
                {MESES[resumen.mes - 1]} {resumen.anio}: {resumen.documentos} documento
                {resumen.documentos === 1 ? '' : 's'} revaluado{resumen.documentos === 1 ? '' : 's'}. Pérdida{' '}
                <MoneyDisplay amount={resumen.totalPerdida} currency="MXN" strong /> · utilidad{' '}
                <MoneyDisplay amount={resumen.totalUtilidad} currency="MXN" strong />. La reversa queda para el{' '}
                {fecha(resumen.fechaReversa)}.
              </p>
              {resumen.omitidas.length > 0 && (
                <p>
                  Sin revaluar por falta de tipo de cambio de registro:{' '}
                  {resumen.omitidas.map((o) => o.documento).join(', ')}.
                </p>
              )}
            </div>
          )}
        </section>
      )}

      <section aria-labelledby="revaluaciones-corridas" className="space-y-2">
        <h2 id="revaluaciones-corridas" className="text-base font-semibold text-ink">
          Revaluaciones corridas
        </h2>
        {lista.isError ? (
          <ErrorState
            title="No se pudieron cargar las revaluaciones"
            problem={esApiError(lista.error) ? lista.error.problem : undefined}
            onRetry={() => lista.refetch()}
          />
        ) : lista.isLoading ? (
          <TableSkeleton rows={3} columns={[{ width: 'w-32' }, { width: 'w-28' }, { width: 'w-28' }, { width: 'w-28' }]} />
        ) : items.length === 0 ? (
          <EmptyState title="Aún no hay revaluaciones" description="Corre la revaluación del mes que cerraste para verla aquí." />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50">
                <tr>
                  <th className="px-3 py-2 text-left">Mes</th>
                  <th className="px-3 py-2 text-left">Reversa automática</th>
                  <th className="px-3 py-2 text-right">Documentos</th>
                  <th className="px-3 py-2 text-right">Pérdida</th>
                  <th className="px-3 py-2 text-right">Utilidad</th>
                  <th className="px-3 py-2 text-left">Acciones</th>
                </tr>
              </thead>
              <tbody>
                {items.map((r) => (
                  <tr key={r.id} className="border-t hover:bg-muted/30">
                    <td className="px-3 py-2">{MESES[r.mes - 1]} {r.anio}</td>
                    <td className="px-3 py-2">
                      {fecha(r.fechaReversa)}{' '}
                      <Badge variant={r.revertida ? 'success' : 'warning'}>
                        {r.revertida ? 'Revertida' : 'Pendiente'}
                      </Badge>
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">{r.documentos}</td>
                    <td className="px-3 py-2 text-right tabular-nums"><MoneyDisplay amount={r.totalPerdida} currency="MXN" strong /></td>
                    <td className="px-3 py-2 text-right tabular-nums"><MoneyDisplay amount={r.totalUtilidad} currency="MXN" strong /></td>
                    <td className="px-3 py-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-label={`Ver reporte de ${MESES[r.mes - 1]} ${r.anio}`}
                        onClick={() => setSeleccion({ anio: r.anio, mes: r.mes })}
                      >
                        Ver reporte
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {seleccion && (
        <ReporteShell
          reporte={reporte.data ? conDecimales(backendToShellShape(reporte.data)) : undefined}
          isLoading={reporte.isLoading}
          error={esApiError(reporte.error) ? reporte.error : undefined}
          onRetry={() => reporte.refetch()}
          nombreArchivo={`revaluacion-cierre-${seleccion.anio}-${String(seleccion.mes).padStart(2, '0')}`}
        />
      )}
    </div>
  );
}
