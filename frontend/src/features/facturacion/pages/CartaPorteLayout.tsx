import { useMemo, type ReactNode } from 'react';
import { useSearch } from '@tanstack/react-router';
import { Inbox } from 'lucide-react';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useListarCartaPorte } from '@/features/facturacion/api/useCartaPorte';
import { esApiError } from '@/lib/api';
import { ListaCartaPorteCompacta } from '@/features/facturacion/components/ListaCartaPorteCompacta';
import type { CartaPorteSearch } from '@/features/facturacion/lib/carta-porte-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>Master-detail de Carta Porte</c> (FAC-UX-PR6) — réplica del
 * exemplar <c>RequisicionesLayout</c> (§6.1). La bandeja tabular sigue
 * en el index (§6.6); este layout monta solo en
 * <c>/facturacion/carta-porte/$id</c>.
 */
export interface CartaPorteLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function CartaPorteLayout({ idActivo, detalle }: CartaPorteLayoutProps) {
  const search = useSearch({ strict: false }) as CartaPorteSearch;
  const query = useListarCartaPorte(search.estado);

  const items = useMemo(() => {
    const todos = query.data ?? [];
    if (!search.q) return todos;
    const needle = search.q.toLowerCase();
    return todos.filter(
      (c) =>
        c.folio.toLowerCase().includes(needle) ||
        c.tramo.toLowerCase().includes(needle),
    );
  }, [query.data, search.q]);

  return (
    <div className="flex flex-col gap-4 px-4 py-6 md:h-[calc(100vh-3.5rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de Cartas Porte"
        data-print="hidden"
      >
        <h1 className="text-xl font-semibold tracking-tight">Carta Porte</h1>
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
              title="Sin Cartas Porte con los filtros actuales."
              description="Ajusta los filtros desde la bandeja de Carta Porte."
            />
          ) : (
            <ListaCartaPorteCompacta items={items} idActivo={idActivo} search={search} />
          )}
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de la Carta Porte"
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
        <p className="text-sm">Selecciona una Carta Porte de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
