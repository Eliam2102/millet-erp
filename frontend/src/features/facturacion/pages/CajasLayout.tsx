import { useMemo, type ReactNode } from 'react';
import { useSearch } from '@tanstack/react-router';
import { Wallet } from 'lucide-react';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useListarCajas } from '@/features/facturacion/api/useCajas';
import { esApiError } from '@/lib/api';
import { ListaCajasCompacta } from '@/features/facturacion/components/ListaCajasCompacta';
import type { CajasSearch } from '@/features/facturacion/lib/cajas-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>Master-detail de cajas</c> (CAJAS-PR5) — réplica de
 * <c>FacturasLayout</c> (§6.1 patrones-compras). La bandeja tabular vive
 * en el index (§6.6); este layout monta solo en
 * <c>/facturacion/cajas/$id</c>.
 */
export interface CajasLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function CajasLayout({ idActivo, detalle }: CajasLayoutProps) {
  const search = useSearch({ strict: false }) as CajasSearch;

  const query = useListarCajas(search.soloActivas ?? false);

  const items = useMemo(() => {
    const todas = query.data ?? [];
    if (!search.q) return todas;
    const needle = search.q.toLowerCase();
    return todas.filter(
      (c) =>
        c.nombre.toLowerCase().includes(needle) ||
        (c.descripcion ?? '').toLowerCase().includes(needle),
    );
  }, [query.data, search.q]);

  return (
    <div className="flex flex-col gap-4 px-4 py-6 md:h-[calc(100vh-3.5rem)] md:flex-row">
      {/* ── Master (lista) ────────────────────────────── */}
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de cajas"
        data-print="hidden"
      >
        <h1 className="text-xl font-semibold tracking-tight">Cajas</h1>
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
              icon={<Wallet className="h-10 w-10" />}
              title={
                search.q
                  ? `Ninguna caja coincide con "${search.q}".`
                  : 'Sin cajas con los filtros actuales.'
              }
              description="Crea una con «Nueva caja» desde la bandeja."
            />
          ) : (
            <ListaCajasCompacta items={items} idActivo={idActivo} search={search} />
          )}
        </div>
      </aside>

      {/* ── Detail panel ──────────────────────────────── */}
      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de la caja"
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
        <Wallet className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una caja de la lista</p>
        <p className="text-xs">para ver y editar su alcance.</p>
      </div>
    </div>
  );
}
