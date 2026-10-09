import { Link, useParams, useSearch } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState } from '@/components/erp';
import { usePropuestaAplicacion } from '@/features/cxc/api/useAplicaciones';
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import { ChipEstadoPropuesta } from '@/features/cxc/components/ChipEstadoPropuesta';
import { EstadoPropuestaAplicacion, type PropuestaAplicacionResponse } from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import type { AplicacionesSearch } from '@/features/cxc/lib/aplicaciones-search-schema';
import { esApiError } from '@/lib/api';

export function DetalleAplicacion() {
  const { id } = useParams({ from: '/_app/cxc/aplicaciones/$id' });
  const query = usePropuestaAplicacion(id);

  return (
    <div className="space-y-4">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la propuesta"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-muted" />
        </div>
      ) : (
        <Contenido p={query.data} />
      )}
    </div>
  );
}

function Contenido({ p }: { p: PropuestaAplicacionResponse }) {
  const search = useSearch({ strict: false }) as AplicacionesSearch;
  const lookup = useClientesLookupCxc({ ids: [p.clienteId] });
  const cliente = lookup.data?.[0] ?? null;
  const sumaAplicada = p.facturas.reduce((a, f) => a + f.importeAplicado, 0);

  return (
    <div className="space-y-6">
      {/* ── Sub-topbar §6.5 ─────────────────────────────────────── */}
      <div
        className="sticky top-0 z-10 flex flex-wrap items-center justify-between gap-2 border-b bg-background/95 pb-2 backdrop-blur"
        data-print="hidden"
      >
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <h1 className="truncate font-mono text-lg font-semibold">
            {p.depositoRef}
          </h1>
          <ChipEstadoPropuesta estado={p.estado} />
        </div>
        <div className="flex items-center gap-1.5">
          <Button variant="ghost" size="icon" asChild aria-label="Cerrar detalle">
            <Link to="/cxc/aplicaciones" search={search}>
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </div>

      {p.estado === EstadoPropuestaAplicacion.Rechazada && p.motivoRechazo && (
        <div className="rounded-md border border-line bg-danger-bg px-4 py-3 text-sm text-danger-fg">
          <p className="font-medium">Propuesta rechazada</p>
          <p>{p.motivoRechazo}</p>
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* ── Depósito ─────────────────────────────────────────── */}
        <section className="rounded-md border bg-card p-4" aria-label="Depósito">
          <h3 className="text-sm font-medium">Depósito</h3>
          <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <div>
              <dt className="text-xs text-muted-foreground">Cliente</dt>
              <dd className="truncate">
                {cliente?.razonSocial ?? p.clienteId.slice(0, 8)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Monto</dt>
              <dd className="font-mono tabular-nums">
                {formatoMonto(p.montoDeposito, p.moneda)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Remittance</dt>
              <dd className="truncate text-xs">{p.remittanceRef}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">
                Ajuste no fiscal
              </dt>
              <dd className="font-mono tabular-nums">
                {p.ajusteNoFiscal !== 0
                  ? formatoMonto(p.ajusteNoFiscal, p.moneda)
                  : '—'}
              </dd>
            </div>
          </dl>
          {p.estado !== EstadoPropuestaAplicacion.Rechazada && (p.saldoAFavorPorIdentificar ?? 0) > 0 && (
            <p className="mt-3 rounded-md bg-info-bg p-3 text-sm text-info-fg">
              Saldo a favor por identificar: {formatoMonto(p.saldoAFavorPorIdentificar ?? 0, p.moneda)}.
              {p.estado === EstadoPropuestaAplicacion.Propuesta ? ' Pendiente de confirmación bancaria.' : ''}
            </p>
          )}
          {p.ajusteNoFiscal !== 0 && (
            <p className="mt-3 text-xs text-warning-fg">
              Diferencia dentro de tolerancia registrada como ajuste no
              fiscal (no genera CFDI).
            </p>
          )}
        </section>

        {/* ── Matching propuesto ───────────────────────────────── */}
        <section
          className="rounded-md border bg-card p-4"
          aria-label="Facturas propuestas"
        >
          <h3 className="text-sm font-medium">Facturas propuestas</h3>
          <table className="mt-3 w-full text-sm">
            <thead className="text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="py-1 font-medium">Factura</th>
                <th className="py-1 text-right font-medium">Importe</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {p.facturas.map((f) => (
                <tr key={f.facturaUuid}>
                  <td className="max-w-56 truncate py-1.5 font-mono text-xs">
                    {f.folio ?? f.facturaUuid}
                    {f.numParcialidad != null && (
                      <span className="ml-1 text-muted-foreground">
                        (parc. {f.numParcialidad})
                      </span>
                    )}
                  </td>
                  <td className="py-1.5 text-right font-mono tabular-nums">
                    {formatoMonto(f.importeAplicado, p.moneda)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t">
                <td className="py-1.5 text-xs font-medium">Σ aplicado</td>
                <td className="py-1.5 text-right font-mono font-medium tabular-nums">
                  {formatoMonto(sumaAplicada, p.moneda)}
                </td>
              </tr>
            </tfoot>
          </table>
        </section>
      </div>

      <p className="text-sm text-ink-muted">Tesorería confirma o rechaza contra el movimiento bancario. La cartera se actualiza al timbrar el REP.</p>
    </div>
  );
}
