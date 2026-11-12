import { MaestroDetalle } from '@/components/erp/MaestroDetalle';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { useListarFacturas } from '@/features/facturacion/api/useFacturas';
import { ListaFacturasCompacta } from '@/features/facturacion/components/ListaFacturasCompacta';
import type { FacturasSearch } from '@/features/facturacion/lib/facturas-search-schema';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import { useSearch } from '@tanstack/react-router';
import { Inbox } from 'lucide-react';
import type { ReactNode } from 'react';

/**
 * <c>Master-detail de comprobantes emitidos</c> (FAC-UX-PR5) — réplica
 * del exemplar <c>RequisicionesLayout</c> (§6.1 patrones-compras). La
 * bandeja tabular sigue viviendo en el index (§6.6); este layout monta
 * solo en <c>/facturacion/facturas/$id</c>.
 */
export interface FacturasLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function FacturasLayout({ idActivo, detalle }: FacturasLayoutProps) {
  const search = useSearch({ strict: false }) as FacturasSearch;

  const query = useListarFacturas({
    estado: search.estado,
    clase: search.clase,
    q: search.q?.trim() || undefined,
    limit: search.limit ?? 200,
  });

  const items = query.data?.items ?? [];

  return (
    <MaestroDetalle
      seleccionado={idActivo != null}
      etiquetaLista="facturas"
      lista={
        <aside
          className={cn(
            'flex min-h-0 flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
            idActivo != null ? 'hidden md:flex' : 'flex',
          )}
          aria-label="Lista de facturas"
          data-print="hidden"
        >
          <PageHeader
            titulo={<>Facturas</>}
            descripcion={<>Consulta los registros y selecciona uno para ver su detalle.</>}
          />
          <div className="flex-1 overflow-y-auto rounded-md border bg-surface-card border-line">
            {query.isLoading ? (
              <div className="p-3">
                <TableSkeleton rows={8} columns={[{ width: 'w-full' }, { width: 'w-full' }]} />
              </div>
            ) : query.isError ? (
              <ErrorState
                problem={esApiError(query.error) ? query.error.problem : undefined}
                onRetry={() => query.refetch()}
              />
            ) : items.length === 0 ? (
              <EmptyState
                icon={<Inbox className="h-10 w-10" />}
                title={
                  search.q
                    ? `Ninguna factura coincide con "${search.q}".`
                    : 'Sin facturas con los filtros actuales.'
                }
                description="Ajusta los filtros desde la bandeja de facturas."
              />
            ) : (
              <ListaFacturasCompacta items={items} idActivo={idActivo} search={search} />
            )}
          </div>
        </aside>
      }
    >
      <section
        className={cn(
          '@container min-h-0 min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de la factura"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </MaestroDetalle>
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-surface-muted/20 p-8 text-center border-line">
      <div className="space-y-1 text-ink-muted">
        <Inbox className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una factura de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
