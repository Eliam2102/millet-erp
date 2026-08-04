import { useMemo, type ReactNode } from 'react';
import { useSearch } from '@tanstack/react-router';
import { Inbox } from 'lucide-react';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useBandejaFacturasAnticipo } from '@/features/facturacion/api/useAnticipos';
import { esApiError } from '@/lib/api';
import { ListaFacturasAnticipoCompacta } from '@/features/facturacion/components/ListaFacturasAnticipoCompacta';
import type { FacturasAnticipoSearch } from '@/features/facturacion/lib/facturas-anticipo-search-schema';
import { cn } from '@/lib/utils';

/**
 * Master-detail de facturas de anticipo (ANT-PR2, doc 13 §6 — patrón P3,
 * molde <c>FacturasLayout</c>). La bandeja tabular vive en el index; este
 * layout monta solo en <c>/facturacion/anticipos/facturas/$id</c>.
 */
export interface FacturasAnticipoLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function FacturasAnticipoLayout({ idActivo, detalle }: FacturasAnticipoLayoutProps) {
  const search = useSearch({ strict: false }) as FacturasAnticipoSearch;

  const query = useBandejaFacturasAnticipo({
    estado: search.estado,
    limit: search.limit ?? 200,
  });

  const items = useMemo(() => {
    const todos = query.data?.items ?? [];
    if (!search.q) return todos;
    const needle = search.q.toLowerCase();
    return todos.filter(
      (f) =>
        f.folio.toLowerCase().includes(needle) ||
        (f.uuid ?? '').toLowerCase().includes(needle) ||
        f.receptorNombre.toLowerCase().includes(needle) ||
        f.receptorRfc.toLowerCase().includes(needle),
    );
  }, [query.data, search.q]);

  return (
    <div className="flex flex-col gap-4 px-4 py-6 md:h-[calc(100vh-3.5rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de facturas de anticipo"
        data-print="hidden"
      >
        <h1 className="text-xl font-semibold tracking-tight">Facturas de anticipo</h1>
        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
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
                  ? `Ninguna factura de anticipo coincide con "${search.q}".`
                  : 'Sin facturas de anticipo con los filtros actuales.'
              }
              description="Ajusta los filtros desde la bandeja."
            />
          ) : (
            <ListaFacturasAnticipoCompacta items={items} idActivo={idActivo} search={search} />
          )}
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de la factura de anticipo"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Inbox className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una factura de anticipo de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
