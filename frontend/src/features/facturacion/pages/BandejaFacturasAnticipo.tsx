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
import { useBandejaFacturasAnticipo } from '@/features/facturacion/api/useAnticipos';
import { BadgeSinAsignar } from '@/features/facturacion/components/BadgeSinAsignar';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import type { FacturasAnticipoSearch } from '@/features/facturacion/lib/facturas-anticipo-search-schema';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { esApiError } from '@/lib/api';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { estadoUi } from '../lib/estados-ui';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

const FROM = '/_app/facturacion/anticipos/facturas/' as const;

/**
 * <c>Bandeja de facturas de anticipo</c> (ANT-PR2, doc 13 §6 — patrón P1,
 * molde <c>BandejaFacturas</c>). Filtro por estado de timbrado server-side +
 * búsqueda client-side; columnas de saldo y estado del anticipo (13-H).
 * "Ver" abre el master-detail <c>/facturacion/anticipos/facturas/$id</c>.
 */
export function BandejaFacturasAnticipo() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const query = useBandejaFacturasAnticipo({
    estado: search.estado,
    limit: 200,
    alcance: search.alcance,
  });

  function actualizarSearch(parcial: Partial<FacturasAnticipoSearch>) {
    navigate({
      to: '/facturacion/anticipos/facturas',
      search: { ...search, ...parcial },
    });
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
        titulo={<>Facturas de anticipo</>}
        descripcion={<>CFDIs de anticipo y el saldo amortizable de cada uno.</>}
        acciones={
          <>
            <Button variant="outline" asChild>
              <Link to="/facturacion/anticipos">Control de Anticipos</Link>
            </Button>
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={Object.entries(EstadoTimbrado)
              .filter(([, valor]) => valor !== EstadoTimbrado.PendientePedimento)
              .map(([nombre, valor]) => ({
                valor,
                label: estadoUi('factura', nombre).texto,
              }))}
            valor={search.estado}
            onChange={(estado) =>
              actualizarSearch({ estado: estado as FacturasAnticipoSearch['estado'] })
            }
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
            title="No se pudieron cargar las facturas de anticipo"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-28' },
              { width: 'w-48' },
              { width: 'w-24' },
              { width: 'w-28' },
              { width: 'w-28' },
              { width: 'w-28' },
              { width: 'w-16' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            title="Sin facturas de anticipo"
            description={
              search.estado != null || q.length > 0
                ? 'No hay facturas de anticipo que coincidan con los filtros.'
                : 'Emite una desde Quick Create → Anticipo.'
            }
          />
        ) : (
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Folio</TableHead>
                  <TableHead className="px-3 py-2 text-left">Receptor</TableHead>
                  <TableHead className="px-3 py-2 text-left">Tipo</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Total</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Saldo</TableHead>
                  <TableHead className="px-3 py-2 text-left">CFDI</TableHead>
                  <TableHead className="px-3 py-2 text-left">Anticipo</TableHead>
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
                    <TableCell className="px-3 py-2 text-xs">{f.tipoAnticipo}</TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {f.total.toFixed(2)} {f.moneda}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {f.saldo != null ? f.saldo.toFixed(2) : '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <ChipTimbrado estado={f.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <ChipEstadoAnticipo estado={f.estadoAnticipo} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right tabular-nums">
                      <Link
                        to="/facturacion/anticipos/facturas/$id"
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

/** Chip del estado del saldo (Abierto / Amortizado / Cancelado, §6.6). */
export function ChipEstadoAnticipo({ estado }: { estado: string | null }) {
  return <EstadoFacturacionBadge entidad="anticipo" estado={estado} />;
}
