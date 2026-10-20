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
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { EstadoPedidoFacturable, OrigenPedido } from '@/features/facturacion/api/types';
import { useListarPedidos } from '@/features/facturacion/api/usePedidos';
import { BadgeSinAsignar } from '@/features/facturacion/components/BadgeSinAsignar';
import { ClasePedidoBadge } from '@/features/facturacion/components/ClasePedidoBadge';
import { FiltroClasePedido } from '@/features/facturacion/components/FiltroClasePedido';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import { useNuevoPedido } from '@/features/facturacion/components/nuevo-pedido-context';
import {
  ETIQUETA_ORIGEN_PEDIDO,
  OPCIONES_ORIGEN_PEDIDO,
} from '@/features/facturacion/lib/glosario';
import type { PedidosSearch } from '@/features/facturacion/lib/pedidos-search-schema';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { estadoUi } from '../lib/estados-ui';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

const FROM = '/_app/facturacion/pedidos/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>Bandeja de pedidos facturables</c> (FE-F1-PR1). Tabla filtrada por
 * estado/origen (server-side) + búsqueda libre client-side por folio /
 * cliente (param <c>q</c> del topbar). Botón "Nuevo pedido" abre el
 * Sheet de captura manual (provider shell-level).
 *
 * <para>La acción "Facturar" (toma soft-lock → emisión) y el detalle de
 * pedido llegan cuando el backend exponga <c>GET /{id}</c> +
 * emisión-desde-pedido (backend F3). En FE-F1 la bandeja es lista +
 * captura.</para>
 */
export function BandejaPedidos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const nuevoPedido = useNuevoPedido();
  const puedeCapturar = useHasPermission(PermisosCanonicos.FacturacionPedidosCapturar);

  const query = useListarPedidos({
    estado: search.estado,
    origen: search.origen,
    clase: search.clase,
    limit: 200,
    alcance: search.alcance,
  });

  function actualizarSearch(parcial: Partial<PedidosSearch>) {
    navigate({ to: '/facturacion/pedidos', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter((p) => {
    if (q.length === 0) return true;
    return (
      (p.numeroPedido ?? '').toLowerCase().includes(q) || p.clienteNombre.toLowerCase().includes(q)
    );
  });

  const hayFiltros =
    search.estado != null || search.origen != null || search.clase != null;

  const paginacion = usePaginacionLocal(items, JSON.stringify(search));
  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Pedidos facturables"
        descripcion="Pedidos por facturar de A+W, Planta Pintura y captura manual."
        acciones={
          <>
            {' '}
            {puedeCapturar && (
              <Button onClick={() => nuevoPedido.abrir()}>
                <Plus className="mr-2 h-4 w-4" />
                Nuevo pedido
              </Button>
            )}{' '}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={Object.entries(EstadoPedidoFacturable).map(([nombre, valor]) => ({
              valor,
              label: estadoUi('pedido', nombre).texto,
            }))}
            valor={search.estado}
            onChange={(estado) => actualizarSearch({ estado: estado as PedidosSearch['estado'] })}
            conteos={undefined}
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <BusquedaFolio
                aria-label="Folio de pedido o cliente"
                placeholder="Folio de pedido o cliente"
                valor={search.q}
                onCommit={(q) => actualizarSearch({ q })}
              />
            }
            filtros={
              <>
                <div className="space-y-1">
                  <label className="text-xs text-ink-muted">Origen</label>
                  <Select
                    value={search.origen != null ? String(search.origen) : SENTINEL_ALL}
                    onValueChange={(v) =>
                      actualizarSearch({
                        origen: v === SENTINEL_ALL ? undefined : (Number(v) as OrigenPedido),
                      })
                    }
                  >
                    <SelectTrigger aria-label="Filtrar por origen" className="w-44">
                      <SelectValue placeholder="Todos" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
                      {OPCIONES_ORIGEN_PEDIDO.map((o) => (
                        <SelectItem key={o.value} value={String(o.value)}>
                          {o.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <FiltroClasePedido
                  value={search.clase}
                  onChange={(clase) => actualizarSearch({ clase })}
                />
                {hayFiltros && (
                  <Button
                    variant="ghost"
                    onClick={() =>
                      actualizarSearch({ estado: undefined, origen: undefined, clase: undefined })
                    }
                  >
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
            title="No se pudieron cargar los pedidos"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[
              { width: 'w-28' },
              { width: 'w-24' },
              { width: 'w-48' },
              { width: 'w-32' },
              { width: 'w-28' },
              { width: 'w-28' },
            ]}
          />
        ) : items.length === 0 ? (
          <EmptyState
            title="Sin pedidos"
            description={
              hayFiltros || q.length > 0
                ? 'No hay pedidos que coincidan con los filtros.'
                : 'Aún no hay pedidos. Crea uno con "Nuevo pedido".'
            }
          />
        ) : (
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Folio / Pedido</TableHead>
                  <TableHead className="px-3 py-2 text-left">Origen / clase</TableHead>
                  <TableHead className="px-3 py-2 text-left">Cliente</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">
                    Subtotal sin impuestos
                  </TableHead>
                  <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                  <TableHead className="px-3 py-2 text-left">Creado</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginacion.visibles.map((p) => (
                  <TableRow key={p.id} className="border-t hover:bg-surface-muted/30 border-line">
                    <TableCell className="px-3 py-2 font-mono text-xs">
                      {p.numeroPedido ?? '—'}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <div className="flex flex-wrap items-center gap-1">
                        <Chip>{ETIQUETA_ORIGEN_PEDIDO[p.origen] ?? p.origen}</Chip>
                        <ClasePedidoBadge clasificacion={p} />
                      </div>
                    </TableCell>
                    <TableCell className="px-3 py-2">{p.clienteNombre}</TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatearMonto(p.total, p.moneda)}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <EstadoFacturacionBadge entidad="pedido" estado={p.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-xs text-ink-muted">
                      {formatearFecha(p.createdAt)}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right tabular-nums">
                      <Link
                        to="/facturacion/pedidos/$id"
                        params={{ id: p.id }}
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

function Chip({ children }: { children: React.ReactNode }) {
  return <Badge variant="neutral">{children}</Badge>;
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

function formatearFecha(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('es-MX', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  } catch {
    return iso;
  }
}
