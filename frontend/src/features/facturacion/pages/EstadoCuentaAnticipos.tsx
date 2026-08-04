import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState } from '@/components/erp';
import { useEstadoCuentaAnticipos } from '@/features/facturacion/api/useAnticipos';
import { esApiError } from '@/lib/api';

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
    <div className="space-y-4 px-4 py-6">
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
          <div className="h-8 w-64 animate-pulse rounded bg-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-muted" />
        </div>
      ) : (
        <Contenido d={query.data} />
      )}
    </div>
  );
}

function Contenido({
  d,
}: {
  d: NonNullable<ReturnType<typeof useEstadoCuentaAnticipos>['data']>;
}) {
  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold">Estado de cuenta de anticipos</h1>
        <p className="font-mono text-xs text-muted-foreground">
          Cliente {d.clienteId}
        </p>
      </header>

      <section className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <Kpi label="Cobrado" valor={d.totalCobrado} />
        <Kpi label="Amortizado" valor={d.totalAmortizado} />
        <Kpi label="Saldo" valor={d.totalSaldo} destacado />
      </section>

      {d.anticipos.length === 0 ? (
        <p className="rounded-md border border-dashed px-3 py-6 text-center text-sm text-muted-foreground">
          El cliente no tiene anticipos.
        </p>
      ) : (
        <div className="space-y-4">
          {d.anticipos.map((a) => (
            <article key={a.anticipoId} className="rounded-md border">
              <header className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/40 px-3 py-2">
                <div className="flex items-center gap-3">
                  <Link
                    to="/facturacion/anticipos/facturas/$id"
                    params={{ id: a.facturaAnticipoId }}
                    className="font-mono text-sm font-semibold text-primary hover:underline"
                  >
                    {a.folio}
                  </Link>
                  <span className="text-xs text-muted-foreground">
                    {a.tipoAnticipo}
                  </span>
                  <span className="rounded-full bg-sky-100 px-2 py-0.5 text-xs font-medium text-sky-800">
                    {a.estado}
                  </span>
                  {a.estadoCfdi !== 'Timbrado' && (
                    <span className="rounded-full bg-rose-100 px-2 py-0.5 text-xs font-medium text-rose-800">
                      CFDI {a.estadoCfdi}
                    </span>
                  )}
                </div>
                <div className="flex gap-4 text-xs tabular-nums">
                  <span>Cobrado {a.montoCobrado.toFixed(2)}</span>
                  <span>Amortizado {a.montoAmortizado.toFixed(2)}</span>
                  <span className="font-semibold">Saldo {a.saldo.toFixed(2)}</span>
                </div>
              </header>
              {a.vinculaciones.length === 0 ? (
                <p className="px-3 py-3 text-xs text-muted-foreground">
                  Sin vinculaciones todavía.
                </p>
              ) : (
                <table className="w-full text-sm">
                  <thead className="bg-muted/20">
                    <tr>
                      <th className="px-3 py-2 text-left">Factura</th>
                      <th className="px-3 py-2 text-right">Importe</th>
                      <th className="px-3 py-2 text-left">NC amortización</th>
                      <th className="px-3 py-2 text-center">NC timbrada</th>
                    </tr>
                  </thead>
                  <tbody>
                    {a.vinculaciones.map((v, i) => (
                      <tr key={`${v.facturaVentaId}-${i}`} className="border-t">
                        <td className="px-3 py-2">
                          <Link
                            to="/facturacion/facturas/$id"
                            params={{ id: v.facturaVentaId }}
                            className="font-mono text-xs text-primary hover:underline"
                          >
                            {v.facturaFolio ?? v.facturaVentaId.slice(0, 8)}
                          </Link>
                        </td>
                        <td className="px-3 py-2 text-right font-mono">
                          {v.importe.toFixed(2)}
                        </td>
                        <td className="px-3 py-2 font-mono text-xs">
                          {v.ncFolio ?? '—'}
                        </td>
                        <td className="px-3 py-2 text-center">
                          {v.ncAmortizacionId == null
                            ? '—'
                            : v.ncTimbrada
                              ? 'Sí'
                              : 'Pendiente'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </article>
          ))}
        </div>
      )}
    </div>
  );
}

function Kpi({
  label,
  valor,
  destacado,
}: {
  label: string;
  valor: number;
  destacado?: boolean;
}) {
  return (
    <div className="rounded-md border px-4 py-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div
        className={
          'font-mono tabular-nums ' +
          (destacado ? 'text-xl font-semibold' : 'text-lg')
        }
      >
        {valor.toFixed(2)}
      </div>
    </div>
  );
}
