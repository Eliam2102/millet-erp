import type { ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * Master-detail de los detalles de Almacén y CxP (patrón P3 de
 * `frontend/docs/patrones-compras.md`): lista compacta de 320 px a la
 * izquierda con el registro activo resaltado + detalle a la derecha. En
 * móvil solo se ve el detalle (drill-down) con un enlace para volver a la
 * bandeja; el aside no se imprime.
 */
interface MasterDetailProps {
  /** Nombre plural del recurso: "Recepciones", "Salidas"… */
  titulo: string;
  /** Ruta de la bandeja a la que vuelve el enlace móvil. */
  bandejaTo: string;
  /** Search de la bandeja: el enlace de regreso conserva filtros y página. */
  bandejaSearch?: object;
  lista: ReactNode;
  children: ReactNode;
}

export function MasterDetail({
  titulo,
  bandejaTo,
  bandejaSearch,
  lista,
  children,
}: MasterDetailProps) {
  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className="hidden flex-col gap-3 md:flex md:w-80 md:shrink-0 md:overflow-hidden"
        aria-label={`Lista de ${titulo.toLowerCase()}`}
        data-print="hidden"
      >
        <div className="flex items-center justify-between gap-2">
          <Link
            to={bandejaTo as never}
            search={(bandejaSearch ?? {}) as never}
            className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
          >
            <ArrowLeft className="size-3.5" aria-hidden="true" />
            {titulo}
          </Link>
        </div>
        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          {lista}
        </div>
      </aside>
      <section
        className="@container min-w-0 flex-1 md:overflow-y-auto"
        aria-label={`Detalle de ${titulo.toLowerCase()}`}
      >
        {children}
      </section>
    </div>
  );
}

export interface ListaCompactaProps<T> {
  query: {
    isLoading: boolean;
    isError: boolean;
    error: unknown;
    refetch: () => unknown;
    data?: { items: T[] };
  };
  idActivo: string;
  to: string;
  /** Search de la bandeja que se arrastra a cada enlace del detalle. */
  search?: object;
  getId: (item: T) => string;
  vacio: string;
  renderItem: (item: T) => { principal: ReactNode; badge: ReactNode; secundario: ReactNode };
}

export function ListaCompacta<T>({
  query,
  idActivo,
  to,
  search,
  getId,
  vacio,
  renderItem,
}: ListaCompactaProps<T>) {
  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton rows={8} columns={[{ width: 'w-full' }, { width: 'w-full' }]} />
      </div>
    );
  }
  if (query.isError) {
    return (
      <ErrorState
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }
  const items = query.data?.items ?? [];
  if (items.length === 0) return <EmptyState title={vacio} />;
  return (
    <ul className="divide-y">
      {items.map((item) => {
        const id = getId(item);
        const activo = id === idActivo;
        const { principal, badge, secundario } = renderItem(item);
        return (
          <li key={id}>
            <Link
              to={to as never}
              params={{ id } as never}
              search={search as never}
              className={cn(
                'block px-3 py-2 transition-colors',
                activo ? 'bg-primary/10 hover:bg-primary/15' : 'hover:bg-muted/40',
              )}
              aria-current={activo ? 'page' : undefined}
            >
              <div className="flex items-start justify-between gap-2">
                <span className={cn('truncate font-mono text-sm', activo && 'font-semibold')}>
                  {principal}
                </span>
                <span className="shrink-0">{badge}</span>
              </div>
              <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
                {secundario}
              </div>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}

