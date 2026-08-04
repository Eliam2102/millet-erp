import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { BadgeSinAsignar } from '@/features/facturacion/components/BadgeSinAsignar';
import { useListarPedidos } from '@/features/facturacion/api/usePedidos';
import { useNuevoPedido } from '@/features/facturacion/components/nuevo-pedido-context';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import {
  ETIQUETA_ESTADO_PEDIDO,
  ETIQUETA_ORIGEN_PEDIDO,
  OPCIONES_ESTADO_PEDIDO,
  OPCIONES_ORIGEN_PEDIDO,
} from '@/features/facturacion/lib/glosario';
import { EstadoPedidoFacturable, OrigenPedido } from '@/features/facturacion/api/types';
import type { PedidosSearch } from '@/features/facturacion/lib/pedidos-search-schema';
import { cn } from '@/lib/utils';

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
  const puedeCapturar = useHasPermission(
    PermisosCanonicos.FacturacionPedidosCapturar,
  );

  const query = useListarPedidos({
    estado: search.estado,
    origen: search.origen,
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
      (p.numeroPedido ?? '').toLowerCase().includes(q) ||
      p.clienteNombre.toLowerCase().includes(q)
    );
  });

  const hayFiltros = search.estado != null || search.origen != null;

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Pedidos facturables
          </h1>
          <p className="text-sm text-muted-foreground">
            Pedidos por facturar de A+W, Planta Pintura y captura manual.
          </p>
        </div>
        {puedeCapturar && (
          <Button onClick={() => nuevoPedido.abrir()}>
            <Plus className="mr-2 h-4 w-4" />
            Nuevo pedido
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Estado</label>
          <Select
            value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                estado:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as EstadoPedidoFacturable),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-48">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              {OPCIONES_ESTADO_PEDIDO.map((o) => (
                <SelectItem key={o.value} value={String(o.value)}>
                  {o.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Origen</label>
          <Select
            value={search.origen != null ? String(search.origen) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                origen:
                  v === SENTINEL_ALL ? undefined : (Number(v) as OrigenPedido),
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
        {hayFiltros && (
          <Button
            variant="ghost"
            onClick={() =>
              actualizarSearch({ estado: undefined, origen: undefined })
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
      </div>

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
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Folio / Pedido</th>
                <th className="px-3 py-2 text-left">Origen</th>
                <th className="px-3 py-2 text-left">Cliente</th>
                <th className="px-3 py-2 text-right">Total</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-left">Creado</th>
                <th className="px-3 py-2 text-right">Acción</th>
              </tr>
            </thead>
            <tbody>
              {items.map((p) => (
                <tr key={p.id} className="border-t hover:bg-muted/30">
                  <td className="px-3 py-2 font-mono text-xs">
                    {p.numeroPedido ?? '—'}
                  </td>
                  <td className="px-3 py-2">
                    <Chip>{ETIQUETA_ORIGEN_PEDIDO[p.origen] ?? p.origen}</Chip>
                  </td>
                  <td className="px-3 py-2">{p.clienteNombre}</td>
                  <td className="px-3 py-2 text-right font-mono">
                    {formatearMonto(p.total, p.moneda)}
                  </td>
                  <td className="px-3 py-2">
                    <Chip estado={p.estado}>
                      {ETIQUETA_ESTADO_PEDIDO[p.estado] ?? p.estado}
                    </Chip>
                  </td>
                  <td className="px-3 py-2 text-xs text-muted-foreground">
                    {formatearFecha(p.createdAt)}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <Link
                      to="/facturacion/pedidos/$id"
                      params={{ id: p.id }}
                      className="text-sm font-medium text-primary hover:underline"
                    >
                      Ver
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function Chip({
  children,
  estado,
}: {
  children: React.ReactNode;
  estado?: string;
}) {
  const tono =
    estado === 'Facturado'
      ? 'bg-emerald-100 text-emerald-800'
      : estado === 'Excepcion'
        ? 'bg-rose-100 text-rose-800'
        : estado === 'Cancelado'
          ? 'bg-zinc-200 text-zinc-700'
          : estado === 'Bloqueado'
            ? 'bg-amber-100 text-amber-800'
            : 'bg-sky-100 text-sky-800';
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        tono,
      )}
    >
      {children}
    </span>
  );
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
