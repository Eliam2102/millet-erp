import { useMemo, type ReactNode } from 'react';
import { useSearch } from '@tanstack/react-router';
import { CreditCard } from 'lucide-react';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  useClientesLookupCxc,
  useLineasCredito,
} from '@/features/cxc/api/useLineasCredito';
import { ListaLineasCompacta } from '@/features/cxc/components/ListaLineasCompacta';
import type { LineasSearch } from '@/features/cxc/lib/lineas-search-schema';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>Master-detail de líneas de crédito</c> (CXC-FE-PR2) — réplica del
 * exemplar <c>FacturasLayout</c> (§6.1 patrones-compras). La bandeja
 * tabular vive en el index (§6.6); este layout monta solo en
 * <c>/cxc/lineas-credito/$id</c>.
 */
export interface LineasCreditoLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function LineasCreditoLayout({ idActivo, detalle }: LineasCreditoLayoutProps) {
  const search = useSearch({ strict: false }) as LineasSearch;

  const query = useLineasCredito({
    estado: search.estado,
    moneda: search.moneda,
    clienteId: search.clienteId,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () =>
      Array.from(
        new Set((query.data?.items ?? []).map((l) => l.clienteId)),
      ).sort(),
    [query.data],
  );
  const lookup = useClientesLookupCxc(
    { ids: clienteIds },
    { enabled: clienteIds.length > 0 },
  );
  const nombres = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of lookup.data ?? []) m.set(c.id, c.razonSocial);
    return m;
  }, [lookup.data]);

  const items = useMemo(() => {
    const todos = query.data?.items ?? [];
    const q = (search.q ?? '').trim().toLowerCase();
    if (!q) return todos;
    return todos.filter((l) =>
      (nombres.get(l.clienteId) ?? l.clienteId).toLowerCase().includes(q),
    );
  }, [query.data, search.q, nombres]);

  return (
    <div className="flex flex-col gap-4 px-4 py-6 md:h-[calc(100vh-3.5rem)] md:flex-row">
      {/* ── Master (lista) ────────────────────────────── */}
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de líneas de crédito"
        data-print="hidden"
      >
        <h1 className="text-xl font-semibold tracking-tight">
          Líneas de crédito
        </h1>
        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          {query.isLoading ? (
            <div className="p-3">
              <TableSkeleton
                rows={8}
                columns={[{ width: 'w-full' }, { width: 'w-full' }]}
              />
            </div>
          ) : query.isError ? (
            <ErrorState
              problem={esApiError(query.error) ? query.error.problem : undefined}
              onRetry={() => query.refetch()}
            />
          ) : items.length === 0 ? (
            <EmptyState
              icon={<CreditCard className="h-10 w-10" />}
              title={
                search.q
                  ? `Ninguna línea coincide con "${search.q}".`
                  : 'Sin líneas con los filtros actuales.'
              }
              description="Ajusta los filtros desde la bandeja de líneas."
            />
          ) : (
            <ListaLineasCompacta
              items={items}
              idActivo={idActivo}
              search={search}
              nombresCliente={nombres}
            />
          )}
        </div>
      </aside>

      {/* ── Detail panel ──────────────────────────────── */}
      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de la línea de crédito"
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
        <CreditCard className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una línea de la lista</p>
        <p className="text-xs">para ver su detalle y crédito disponible.</p>
      </div>
    </div>
  );
}
