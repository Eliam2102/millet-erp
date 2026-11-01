import { KpiCard, ErrorState } from '@/components/erp';

import { useCreditoDisponible } from '@/features/cxc/api/useLineasCredito';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import { esApiError } from '@/lib/api';
import { ChipEstadoLinea } from '@/features/cxc/components/ChipEstadoLinea';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CreditoDisponibleCard/&gt;</c> — crédito disponible del cliente
 * por línea/moneda (CXC-PR2/PR3): <c>disponible = límite − facturado −
 * liberado sin factura</c>. Nunca convierte divisas: un renglón por
 * moneda.
 *
 * <para>C7.1 (86e3kdxx4): el liberado sin facturar ya es real (pedidos a
 * crédito en firme sin factura, menos su anticipo), así que se muestra
 * el desglose completo y ya no hay marca de dato incompleto.</para>
 */
export function CreditoDisponibleCard({
  clienteId,
  className,
}: {
  clienteId: string;
  className?: string;
}) {
  const query = useCreditoDisponible(clienteId);

  return (
    <section
      className={cn('rounded-md border bg-surface-card p-4', className)}
      aria-label="Crédito disponible"
    >
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-medium">Crédito disponible</h3>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo evaluar el crédito disponible"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="mt-3 space-y-2">
          <div className="h-8 w-48 animate-pulse rounded bg-surface-muted" />
          <div className="h-4 w-64 animate-pulse rounded bg-surface-muted" />
        </div>
      ) : query.data.lineas.length === 0 ? (
        <p className="mt-3 text-sm text-ink-muted">El cliente no tiene líneas de crédito.</p>
      ) : (
        <ul className="mt-3 space-y-3">
          {query.data.lineas.map((l) => (
            <li key={l.lineaCreditoId} className="space-y-3">
              <div className="flex items-center justify-between">
                <span className="font-mono">{l.moneda}</span>
                <ChipEstadoLinea estado={l.estado} />
              </div>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <KpiCard etiqueta="Límite" valor={formatoMonto(l.limite, l.moneda)} />
                <KpiCard
                  etiqueta="Facturado"
                  valor={formatoMonto(l.facturado, l.moneda)}
                  subdato="Saldo de facturas abiertas"
                  tono="info"
                />
                <KpiCard
                  etiqueta="Liberado sin facturar"
                  valor={formatoMonto(l.liberadoSinFactura, l.moneda)}
                  subdato="Pedidos a crédito en firme, menos anticipo"
                  tono="info"
                />
                <KpiCard
                  etiqueta="Disponible"
                  valor={formatoMonto(l.disponible, l.moneda)}
                  tono={l.disponible < 0 ? 'danger' : 'success'}
                />
              </div>
              <p className="text-xs text-ink-muted">Plazo {l.plazoDias} días</p>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
