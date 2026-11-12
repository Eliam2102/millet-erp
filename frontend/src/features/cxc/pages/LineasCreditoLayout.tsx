import { MaestroDetalle } from '@/components/erp/MaestroDetalle';
import { PageHeader, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';

import { useMemo, type ReactNode } from 'react';
import { useSearch } from '@tanstack/react-router';
import { CreditCard } from 'lucide-react';

import { useClientesLookupCxc, useLineasCredito } from '@/features/cxc/api/useLineasCredito';
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
    q: search.q,
    limit: search.limit ?? 200,
  });

  const clienteIds = useMemo(
    () => Array.from(new Set((query.data?.items ?? []).map((l) => l.clienteId))).sort(),
    [query.data],
  );
  const lookup = useClientesLookupCxc({ ids: clienteIds }, { enabled: clienteIds.length > 0 });
  const nombres = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of lookup.data ?? []) m.set(c.id, c.razonSocial);
    return m;
  }, [lookup.data]);

  // M1: el texto (razón social, RFC o clave) ya lo filtra el servidor.
  const items = query.data?.items ?? [];

  return (
    <MaestroDetalle
      seleccionado={idActivo != null}
      etiquetaLista="líneas de crédito"
      lista={
        <aside
          className={cn(
            'flex min-h-0 flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
            idActivo != null ? 'hidden md:flex' : 'flex',
          )}
          aria-label="Lista de líneas de crédito"
          data-print="hidden"
        >
          <PageHeader titulo={<>Líneas de crédito</>} />
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
      }
    >
      <section
        className={cn(
          '@container min-h-0 min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de la línea de crédito"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </MaestroDetalle>
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-surface-muted/20 p-8 text-center">
      <div className="space-y-1 text-ink-muted">
        <CreditCard className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una línea de la lista</p>
        <p className="text-xs">para ver su detalle y crédito disponible.</p>
      </div>
    </div>
  );
}
