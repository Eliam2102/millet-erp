import { useState, type FormEvent } from 'react';
import { format, subDays } from 'date-fns';
import { Scale } from 'lucide-react';
import { toast } from 'sonner';
import {
  DateTimeDisplay,
  EmptyState,
  ErrorState,
  PageHeader,
  TableSkeleton,
} from '@/components/erp';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  useConciliacionDetalle,
  useConciliaciones,
  useConciliarCartera,
  type ConciliacionResumen,
} from '@/features/cxc/api/useConciliaciones';
import { ReporteCxc } from '@/features/cxc/components/ReporteCxc';
import { adaptarReporteCxc } from '@/features/cxc/lib/reporte-cxc-adapter';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { codigoSoporte, esApiError } from '@/lib/api';
import { hoyLocalISO, parseDateOnlyLocal } from '@/lib/datetime';
import { cn } from '@/lib/utils';

function ayerLocalISO(): string {
  return format(subDays(parseDateOnlyLocal(hoyLocalISO()), 1), 'yyyy-MM-dd');
}

/**
 * <c>Conciliación de cartera</c> (C7.7, 86e3jvv4q) — <c>/cxc/conciliacion</c>.
 * Cada día a las 06:00 se concilia el día anterior; aquí se ven las corridas,
 * se concilia un día a demanda (con <c>conciliacion.ejecutar</c>) y se revisan
 * las diferencias por cliente con su causa probable.
 */
export function ConciliacionCarteraPage() {
  const corridas = useConciliaciones();
  const [seleccionada, setSeleccionada] = useState<string | null>(null);
  const idActivo = seleccionada ?? corridas.data?.[0]?.id ?? null;

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <header data-print="hidden">
        <PageHeader
          titulo={<>Conciliación de cartera</>}
          descripcion={
            <>
              Compara cada día lo timbrado, cobrado y cancelado en Facturación contra la cartera y
              lista las diferencias por cliente con su causa probable.
            </>
          }
          acciones={<ConciliarDia onConciliada={setSeleccionada} />}
        />
      </header>

      {corridas.isError ? (
        <ErrorState
          title="No se pudieron cargar las conciliaciones"
          problem={esApiError(corridas.error) ? corridas.error.problem : undefined}
          onRetry={() => corridas.refetch()}
        />
      ) : corridas.isLoading ? (
        <TableSkeleton rows={4} />
      ) : (corridas.data ?? []).length === 0 ? (
        <EmptyState
          icon={<Scale className="h-8 w-8" strokeWidth={1.6} />}
          title="Todavía no hay conciliaciones"
          description="La primera corrida programada aparece mañana a las 06:00. También puedes conciliar un día ahora."
        />
      ) : (
        <>
          <TablaCorridas
            corridas={corridas.data ?? []}
            activa={idActivo}
            onSeleccionar={setSeleccionada}
          />
          {idActivo && <DetalleCorrida id={idActivo} />}
        </>
      )}
    </div>
  );
}

function ConciliarDia({ onConciliada }: { onConciliada: (id: string) => void }) {
  const puede = useHasPermission(PermisosCanonicos.CuentasPorCobrarConciliacionEjecutar);
  const conciliar = useConciliarCartera();
  const [fecha, setFecha] = useState(ayerLocalISO);
  const hoy = hoyLocalISO();
  const motivo = !puede
    ? 'Necesitas el permiso para conciliar la cartera.'
    : fecha === '' || fecha > hoy
      ? 'Elige un día que ya haya pasado o el de hoy.'
      : undefined;

  function enviar(e: FormEvent) {
    e.preventDefault();
    if (motivo) return;
    conciliar.mutate(
      { fecha, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (corrida) => {
          onConciliada(corrida.id);
          toast.success(
            corrida.totalDiferencias === 0
              ? 'Conciliación sin diferencias.'
              : `Conciliación con ${corrida.totalDiferencias} diferencia(s). Revísalas abajo.`,
          );
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.problem.detail ?? codigoSoporte(error),
            });
            return;
          }
          toast.error('No se pudo conciliar el día. Revisa tu conexión y vuelve a intentarlo.');
        },
      },
    );
  }

  return (
    <form onSubmit={enviar} className="flex items-end gap-2">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="conciliacion-fecha" className="text-xs font-medium text-ink-strong">
          Día a conciliar
        </Label>
        <Input
          id="conciliacion-fecha"
          type="date"
          max={hoy}
          value={fecha}
          onChange={(e) => setFecha(e.target.value)}
          className="w-40"
        />
      </div>
      <Button type="submit" disabled={motivo != null || conciliar.isPending} title={motivo}>
        {conciliar.isPending ? 'Conciliando…' : 'Conciliar día'}
      </Button>
    </form>
  );
}

function BadgeDiferencias({ total }: { total: number }) {
  return total === 0 ? (
    <Badge variant="success">Sin diferencias</Badge>
  ) : (
    <Badge variant="danger">Con diferencia</Badge>
  );
}

function TablaCorridas({
  corridas,
  activa,
  onSeleccionar,
}: {
  corridas: ConciliacionResumen[];
  activa: string | null;
  onSeleccionar: (id: string) => void;
}) {
  return (
    <div className="overflow-x-auto rounded-lg bg-surface-card shadow-card-flat" data-print="hidden">
      <Table className="w-full text-sm">
        <TableHeader className="bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
          <TableRow>
            <TableHead className="px-3 py-2">Día conciliado</TableHead>
            <TableHead className="px-3 py-2">Origen</TableHead>
            <TableHead className="px-3 py-2">Ejecutada</TableHead>
            <TableHead className="px-3 py-2 text-right">Comprobantes revisados</TableHead>
            <TableHead className="px-3 py-2 text-right">Diferencias</TableHead>
            <TableHead className="px-3 py-2">Resultado</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {corridas.map((c) => (
            <TableRow
              key={c.id}
              className={cn('border-b border-line-row', c.id === activa && 'bg-surface-row-focus')}
            >
              <TableCell className="px-3 py-2">
                <button
                  type="button"
                  className="text-brand hover:underline"
                  aria-current={c.id === activa ? 'true' : undefined}
                  onClick={() => onSeleccionar(c.id)}
                >
                  <DateTimeDisplay value={c.fecha} variant="media" />
                </button>
              </TableCell>
              <TableCell className="px-3 py-2 text-ink-secondary">
                {c.origen === 'Programada' ? 'Programada' : 'A demanda'}
              </TableCell>
              <TableCell className="px-3 py-2 text-ink-secondary">
                <DateTimeDisplay value={c.ejecutadaEn} variant="datetime" />
              </TableCell>
              <TableCell className="px-3 py-2 text-right tabular-nums">{c.comprobantesRevisados}</TableCell>
              <TableCell className="px-3 py-2 text-right font-medium tabular-nums">
                {c.totalDiferencias}
              </TableCell>
              <TableCell className="px-3 py-2">
                <BadgeDiferencias total={c.totalDiferencias} />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function DetalleCorrida({ id }: { id: string }) {
  const detalle = useConciliacionDetalle(id);
  return (
    <ReporteCxc
      reporte={adaptarReporteCxc(detalle.data?.reporte)}
      isLoading={detalle.isLoading}
      error={esApiError(detalle.error) ? detalle.error : undefined}
      onRetry={() => detalle.refetch()}
      filtrosUi={null}
      nombreArchivo={`conciliacion-cartera-${detalle.data?.corrida.fecha ?? ''}`}
    />
  );
}
