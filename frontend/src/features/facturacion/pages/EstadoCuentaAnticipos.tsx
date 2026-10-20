import { estadoUi } from '../lib/estados-ui';
import { ErrorState } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useEstadoCuentaAnticipos } from '@/features/facturacion/api/useAnticipos';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import { esApiError } from '@/lib/api';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';

/**
 * <c>Control de Anticipos — Detallada por cliente</c> (FE-F4-PR2).
 * Estado de cuenta: por cada anticipo, sus vinculaciones a facturas y la
 * NC de amortización (relación 07) con su estado de timbre.
 */
export function EstadoCuentaAnticipos() {
  const { clienteId } = useParams({
    from: '/_app/facturacion/anticipos/$clienteId',
  });
  const query = useEstadoCuentaAnticipos(clienteId);

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between gap-3" data-print="hidden">
        <Button variant="ghost" size="sm" asChild>
          <Link to="/facturacion/anticipos">
            <ArrowLeft className="mr-1 h-4 w-4" />
            Volver al Control de Anticipos
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el estado de cuenta"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-surface-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-surface-muted" />
        </div>
      ) : (
        <Contenido d={query.data} />
      )}
    </div>
  );
}

function Contenido({ d }: { d: NonNullable<ReturnType<typeof useEstadoCuentaAnticipos>['data']> }) {
  return (
    <div className="space-y-5">
      <div className="space-y-1">
        <PageHeader
          mono
          titulo={<>Estado de cuenta de anticipos</>}
          descripcion={<>Cliente {d.clienteId}</>}
        />
      </div>

      <section className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <Kpi label="Cobrado" valor={d.totalCobrado} />
        <Kpi label="Amortizado" valor={d.totalAmortizado} />
        <Kpi label="Saldo" valor={d.totalSaldo} destacado />
      </section>

      {d.anticipos.length === 0 ? (
        <p className="rounded-md border border-dashed px-3 py-6 text-center text-sm text-ink-muted border-line">
          El cliente no tiene anticipos.
        </p>
      ) : (
        <div className="space-y-5">
          {d.anticipos.map((a) => (
            <article key={a.anticipoId} className="rounded-md border border-line">
              <header className="flex flex-wrap items-center justify-between gap-2 border-b bg-surface-muted/40 px-3 py-2 border-line">
                <div className="flex items-center gap-3">
                  <Link
                    to="/facturacion/anticipos/facturas/$id"
                    params={{ id: a.facturaAnticipoId }}
                    className="font-mono text-sm font-semibold text-brand hover:underline"
                  >
                    {a.folio}
                  </Link>
                  <span className="text-xs text-ink-muted">{a.tipoAnticipo}</span>
                  <EstadoFacturacionBadge entidad="anticipo" estado={a.estado} />
                  {a.estadoCfdi !== 'Timbrado' && (
                    <Badge variant={estadoUi('factura', a.estadoCfdi).tono}>
                      CFDI {estadoUi('factura', a.estadoCfdi).texto}
                    </Badge>
                  )}
                </div>
                <div className="flex gap-4 text-xs tabular-nums">
                  <span>Cobrado {a.montoCobrado.toFixed(2)}</span>
                  <span>Amortizado {a.montoAmortizado.toFixed(2)}</span>
                  <span className="font-semibold">Saldo {a.saldo.toFixed(2)}</span>
                </div>
              </header>
              {a.vinculaciones.length === 0 ? (
                <p className="px-3 py-3 text-xs text-ink-muted">Sin vinculaciones todavía.</p>
              ) : (
                <Table className="w-full text-sm">
                  <TableHeader className="bg-surface-muted/20">
                    <TableRow>
                      <TableHead className="px-3 py-2 text-left">Factura</TableHead>
                      <TableHead className="px-3 py-2 text-right tabular-nums">Importe</TableHead>
                      <TableHead className="px-3 py-2 text-left">NC amortización</TableHead>
                      <TableHead className="px-3 py-2 text-center">NC timbrada</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {a.vinculaciones.map((v, i) => (
                      <TableRow key={`${v.facturaVentaId}-${i}`} className="border-t border-line">
                        <TableCell className="px-3 py-2">
                          <Link
                            to="/facturacion/facturas/$id"
                            params={{ id: v.facturaVentaId }}
                            className="font-mono text-xs text-brand hover:underline"
                          >
                            {v.facturaFolio ?? v.facturaVentaId.slice(0, 8)}
                          </Link>
                        </TableCell>
                        <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                          {v.importe.toFixed(2)}
                        </TableCell>
                        <TableCell className="px-3 py-2 font-mono text-xs">
                          {v.ncFolio ?? '—'}
                        </TableCell>
                        <TableCell className="px-3 py-2 text-center">
                          {v.ncAmortizacionId == null ? '—' : v.ncTimbrada ? 'Sí' : 'Pendiente'}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </article>
          ))}
        </div>
      )}
    </div>
  );
}

function Kpi({ label, valor, destacado }: { label: string; valor: number; destacado?: boolean }) {
  return (
    <div className="rounded-md border px-4 py-3 border-line">
      <div className="text-xs text-ink-muted">{label}</div>
      <div
        className={'font-mono tabular-nums ' + (destacado ? 'text-xl font-semibold' : 'text-lg')}
      >
        {valor.toFixed(2)}
      </div>
    </div>
  );
}
