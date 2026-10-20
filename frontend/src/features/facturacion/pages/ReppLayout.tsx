import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { useListarRepp } from '@/features/facturacion/api/useRepp';
import { ListaReppCompacta } from '@/features/facturacion/components/ListaReppCompacta';
import type { ReppSearch } from '@/features/facturacion/lib/repp-search-schema';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import { useSearch } from '@tanstack/react-router';
import { Inbox } from 'lucide-react';
import { useMemo, type ReactNode } from 'react';

/**
 * <c>Master-detail de complementos de pago (REPP)</c> (FAC-UX-PR6) —
 * réplica del exemplar <c>RequisicionesLayout</c> (§6.1). La bandeja
 * tabular sigue en el index (§6.6); este layout monta solo en
 * <c>/facturacion/repp/$id</c>.
 */
export interface ReppLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function ReppLayout({ idActivo, detalle }: ReppLayoutProps) {
  const search = useSearch({ strict: false }) as ReppSearch;
  const query = useListarRepp(search.estado);

  const items = useMemo(() => {
    const todos = query.data?.items ?? [];
    if (!search.q) return todos;
    const needle = search.q.toLowerCase();
    return todos.filter(
      (r) =>
        r.folio.toLowerCase().includes(needle) || r.receptorNombre.toLowerCase().includes(needle),
    );
  }, [query.data, search.q]);

  return (
    <div className="flex flex-col gap-4   md:h-[calc(100vh-3.5rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de complementos de pago"
        data-print="hidden"
      >
        <PageHeader
          titulo={<>REPP</>}
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
              title="Sin complementos de pago con los filtros actuales."
              description="Ajusta los filtros desde la bandeja de REPP."
            />
          ) : (
            <ListaReppCompacta items={items} idActivo={idActivo} search={search} />
          )}
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle del complemento de pago"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-surface-muted/20 p-8 text-center border-line">
      <div className="space-y-1 text-ink-muted">
        <Inbox className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona un REPP de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
