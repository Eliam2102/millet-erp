import { PageHeader, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';

import { useMemo, type ReactNode } from 'react';
import { Link, useSearch } from '@tanstack/react-router';
import { FileText } from 'lucide-react';

import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import { usePropuestasAplicacion } from '@/features/cxc/api/useAplicaciones';
import { ChipEstadoPropuesta } from '@/features/cxc/components/ChipEstadoPropuesta';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import type { AplicacionesSearch } from '@/features/cxc/lib/aplicaciones-search-schema';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>Master-detail de propuestas de aplicación</c> (CXC-FE-PR6) —
 * réplica del exemplar §6.1: lista compacta 320px + detalle; mobile
 * drill-down. La bandeja tabular vive en el index (§6.6).
 */
export interface AplicacionesLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function AplicacionesLayout({ idActivo, detalle }: AplicacionesLayoutProps) {
  const search = useSearch({ strict: false }) as AplicacionesSearch;

  const query = usePropuestasAplicacion({
    estado: search.estado,
    clienteId: search.clienteId,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () => Array.from(new Set((query.data?.items ?? []).map((p) => p.clienteId))).sort(),
    [query.data],
  );
  const lookup = useClientesLookupCxc({ ids: clienteIds }, { enabled: clienteIds.length > 0 });
  const nombres = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of lookup.data ?? []) m.set(c.id, c.razonSocial);
    return m;
  }, [lookup.data]);

  const items = useMemo(() => {
    const todos = query.data?.items ?? [];
    const q = (search.q ?? '').trim().toLowerCase();
    if (!q) return todos;
    return todos.filter(
      (p) =>
        p.depositoRef.toLowerCase().includes(q) ||
        (nombres.get(p.clienteId) ?? '').toLowerCase().includes(q),
    );
  }, [query.data, search.q, nombres]);

  return (
    <div className="flex flex-col gap-5 min-h-0 md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de propuestas"
        data-print="hidden"
      >
        <PageHeader titulo={<>Aplicación de pagos</>} />
        <div className="flex-1 overflow-y-auto rounded-md border bg-surface-card">
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
              icon={<FileText className="h-10 w-10" />}
              title="Sin propuestas con los filtros actuales."
              description="Ajusta los filtros desde la bandeja."
            />
          ) : (
            <ul className="divide-y" role="list" aria-label="Lista de propuestas">
              {items.map((p) => (
                <li key={p.id}>
                  <Link
                    to="/cxc/aplicaciones/$id"
                    params={{ id: p.id }}
                    search={search}
                    state={{ bandejaSearch: search } as never}
                    className={cn(
                      'block px-3 py-2 transition-colors',
                      p.id === idActivo
                        ? 'bg-brand/10 hover:bg-brand/15'
                        : 'hover:bg-surface-muted/40',
                    )}
                    aria-current={p.id === idActivo ? 'page' : undefined}
                  >
                    <div className="flex items-start justify-between gap-2">
                      <span
                        className={cn(
                          'truncate font-mono text-xs',
                          p.id === idActivo && 'font-semibold',
                        )}
                      >
                        {p.depositoRef}
                      </span>
                      <ChipEstadoPropuesta estado={p.estado} />
                    </div>
                    <div className="mt-1 flex items-center justify-between gap-2 text-xs text-ink-muted">
                      <span className="truncate">
                        {nombres.get(p.clienteId) ?? p.clienteId.slice(0, 8)}
                      </span>
                      <span className="shrink-0 font-mono tabular-nums">
                        {formatoMonto(p.montoDeposito, p.moneda)}
                      </span>
                    </div>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de la propuesta"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-surface-muted/20 p-8 text-center">
      <div className="space-y-1 text-ink-muted">
        <FileText className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una propuesta de la lista</p>
        <p className="text-xs">para revisar el matching y resolverla.</p>
      </div>
    </div>
  );
}
