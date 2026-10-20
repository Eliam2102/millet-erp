import {
  BarraFiltros,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  TableSkeleton,
  TabsEstado,
} from '@/components/erp';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { EstadoTimbrado } from '@/features/facturacion/api/types';
import { useListarFacturas } from '@/features/facturacion/api/useFacturas';
import { BadgeSinAsignar } from '@/features/facturacion/components/BadgeSinAsignar';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import type { FacturasSearch } from '@/features/facturacion/lib/facturas-search-schema';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { estadoUi } from '../lib/estados-ui';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

const FROM = '/_app/facturacion/facturas/' as const;

/**
 * <c>Bandeja de comprobantes emitidos</c> (FE-F1-PR2). Tabla filtrada
 * por estado de timbrado (server-side) + búsqueda client-side por folio /
 * UUID / receptor. "Ver" lleva al detalle (master-detail §6.1). "Nueva
 * factura" navega al form de emisión con pestañas (FAC-UX-PR2).
 */
export function BandejaFacturas() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionFacturasEmitir);

  const query = useListarFacturas({
    estado: search.estado,
    limit: 200,
    alcance: search.alcance,
  });

  function actualizarSearch(parcial: Partial<FacturasSearch>) {
    navigate({ to: '/facturacion/facturas', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((f) => {
    if (q.length === 0) return true;
    return (
      f.folio.toLowerCase().includes(q) ||
      (f.uuid ?? '').toLowerCase().includes(q) ||
      f.receptorNombre.toLowerCase().includes(q) ||
      f.receptorRfc.toLowerCase().includes(q)
    );
  });

  const paginacion = usePaginacionLocal(items, JSON.stringify(search));
  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Facturas"
        descripcion="Comprobantes de venta emitidos y su estado de timbrado."
        acciones={
          <>
            {' '}
            {puedeEmitir && (
              <Button asChild>
                <Link to="/facturacion/facturas/nueva">
                  <Plus className="mr-2 h-4 w-4" />
                  Nueva factura
                </Link>
              </Button>
            )}{' '}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={Object.entries(EstadoTimbrado).map(([nombre, valor]) => ({
              valor,
              label: estadoUi('factura', nombre).texto,
            }))}
            valor={search.estado}
            onChange={(estado) => actualizarSearch({ estado: estado as FacturasSearch['estado'] })}
            conteos={undefined}
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <BusquedaFolio
                aria-label="Folio, UUID, receptor o RFC"
                placeholder="Folio, UUID, receptor o RFC"
                valor={search.q}
                onCommit={(q) => actualizarSearch({ q })}
              />
            }
            filtros={
              <>
                {search.estado != null && (
                  <Button variant="ghost" onClick={() => actualizarSearch({ estado: undefined })}>
                    Limpiar
                  </Button>
                )}
                <BadgeSinAsignar
                  count={query.data?.sinAsignarCount ?? null}
                  activo={search.alcance === 'sin-asignar'}
                  onToggle={() =>
                    actualizarSearch({
                      alcance: search.alcance === 'sin-asignar' ? undefined : 'sin-asignar',
                    })
                  }
                />
              </>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={paginacion.offset}
            limit={10}
            tamanos={[]}
            onChange={paginacion.onChange}
          />
        }
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar las facturas"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-28' },
              { width: 'w-48' },
              { width: 'w-40' },
              { width: 'w-32' },
              { width: 'w-28' },
              { width: 'w-16' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            title="Sin facturas"
            description={
              search.estado != null || q.length > 0
                ? 'No hay facturas que coincidan con los filtros.'
                : 'Aún no hay facturas emitidas. Emite una con "Nueva factura".'
            }
          />
        ) : (
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Folio</TableHead>
                  <TableHead className="px-3 py-2 text-left">Receptor</TableHead>
                  <TableHead className="px-3 py-2 text-left">UUID</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Total</TableHead>
                  <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginacion.visibles.map((f) => (
                  <TableRow key={f.id} className="border-t hover:bg-surface-muted/30 border-line">
                    <TableCell className="px-3 py-2 font-mono text-xs">{f.folio}</TableCell>
                    <TableCell className="px-3 py-2">
                      <div>{f.receptorNombre}</div>
                      <div className="font-mono text-xs text-ink-muted">{f.receptorRfc}</div>
                    </TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">
                      {f.uuid ? `${f.uuid.slice(0, 8)}…${f.uuid.slice(-4)}` : '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatearMonto(f.total, f.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <ChipTimbrado estado={f.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right tabular-nums">
                      <Link
                        to="/facturacion/facturas/$id"
                        params={{ id: f.id }}
                        className="text-sm font-medium text-brand hover:underline"
                      >
                        Ver
                      </Link>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
    </div>
  );
}

export function ChipTimbrado({ estado }: { estado: string }) {
  return <EstadoFacturacionBadge entidad="factura" estado={estado} />;
}

function formatearMonto(v: number, moneda: string): string {
  try {
    return new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: moneda,
      minimumFractionDigits: 2,
    }).format(v);
  } catch {
    return `${v.toFixed(2)} ${moneda}`;
  }
}
