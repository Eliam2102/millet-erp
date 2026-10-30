import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { TabsEstado } from '@/components/erp/data/TabsEstado';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  EstadoBadge,
  DateTimeDisplay,
  SortableHeader,
  compareItemsBy,
  type SortState,
  PaginacionTabla,
} from '@/components/erp';
import { useOrdenesCompra } from '@/features/compras/ordenes/api/useOrdenesCompra';
import { useProveedores, useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { FiltrosBandejaOc } from '@/features/compras/ordenes/components/FiltrosBandejaOc';
import { HoverSubEstados } from '@/features/compras/ordenes/components/HoverSubEstados';
import { useNuevaOrdenCompra } from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import {
  estadoOcToString,
  ESTADO_COMERCIAL_PROVEEDOR_LABELS,
  EstadoComercialProveedorOc,
  EstadoOrdenCompra,
  type OrdenCompraResumen,
} from '@/features/compras/ordenes/api/types';
import { type BandejaOcSearch } from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';
import { PageHeader } from '@/components/erp/PageHeader';

// Path interno del routeTree (incluye el layout `_app`). Mismo patrón
// que <c>BandejaRequisiciones</c>; el path público que usa <c>Link</c>
// es <c>/compras/ordenes</c>.
const BANDEJA_FROM = '/_app/compras/ordenes/' as const;

/**
 * <c>P1 Bandeja general de Órdenes de Compra</c> — vista tabular
 * full-page (frontend/docs/patrones-compras.md §6.6).
 *
 * <para>Mismo patrón cross-módulo que
 * <c>&lt;BandejaRequisiciones/&gt;</c>: tabla densa con columnas
 * escaneables (folio, fecha, proveedor, comprador, estado, sub-estados,
 * acción "Ver"), sort client-side por columnas Folio/Fecha/Estado,
 * paginación page/pageSize del backend, búsqueda por folio
 * client-side sobre la página actual (el <c>q</c> del topbar global
 * además se manda al endpoint como <c>referenciaProveedor</c>).</para>
 *
 * <para>Click en "Ver" navega a <c>/compras/ordenes/$id</c> que monta
 * el master-detail (<c>OrdenesCompraLayout</c> + <c>DetalleOrdenCompra</c>)
 * preservando los filtros via <c>state.bandejaOcSearch</c>.</para>
 *
 * <para><b>Sin botón "Nueva OC"</b> — el Sheet de creación llega en
 * UF2-PR1 con su provider <c>useNuevaOrdenCompra</c>. Convención
 * §6.4 patrones-compras: no agregar Quick Create / botón "Nueva ..."
 * sin el Sheet correspondiente.</para>
 *
 * <para>Resolución de IDs → nombres: <c>useProveedores</c> /
 * <c>useUsuarios</c> con cache 1h. Para proveedores prefiere
 * <c>nombreComercial</c> (amigable) y cae a <c>razonSocial</c>; si el
 * id no resuelve (catálogo no cargado o el item no está en la página
 * cacheada), fallback al id raw — coherente con resto del frontend.</para>
 */
export function BandejaOrdenesCompra() {
  const search = useSearch({ from: BANDEJA_FROM });
  const navigate = useNavigate();
  const nuevaOC = useNuevaOrdenCompra();
  const canCrear = useHasPermission(PermisosCanonicos.ComprasOrdenesCrear);

  const ordenesQuery = useOrdenesCompra({
    estado: search.estado,
    subEstadoRecepcion: search.subEstadoRecepcion,
    subEstadoFacturacion: search.subEstadoFacturacion,
    subEstadoPago: search.subEstadoPago,
    estadoComercialProveedor: search.estadoComercialProveedor,
    proveedorId: search.proveedorId,
    compradorTitularId: search.compradorTitularId,
    fechaDesde: search.fechaDesde,
    fechaHasta: search.fechaHasta,
    referenciaProveedor: search.q,
    page: search.page,
    pageSize: search.pageSize,
  });
  // Cargados con limit alto + includeInactivas para resolver
  // proveedores históricos en la bandeja (un proveedor pudo haberse
  // inactivado tras crear una OC). Sin esto, la bandeja muestra el
  // GUID en la columna proveedor.
  const proveedoresQuery = useProveedores({
    limit: 1000,
    includeInactivas: true,
  });
  const usuariosQuery = useUsuarios();

  const proveedoresMap = useMemo(
    () => mapById(proveedoresQuery.data?.items),
    [proveedoresQuery.data],
  );
  const usuariosMap = useMemo(() => mapById(usuariosQuery.data?.items), [usuariosQuery.data]);

  // Búsqueda por folio: client-side sobre la página actual (el `q`
  // del topbar también se manda como `referenciaProveedor` server-side
  // en el hook; este filtro adicional cubre el matching por folio
  // dentro de los items ya cargados).
  const itemsFiltrados = useMemo(() => {
    const items = ordenesQuery.data?.items ?? [];
    if (!search.q) return items;
    const needle = search.q.toLowerCase();
    return items.filter((o) => o.folio.toLowerCase().includes(needle));
  }, [ordenesQuery.data, search.q]);

  function setSearch(next: BandejaOcSearch) {
    navigate({
      to: '/compras/ordenes',
      search: next,
      replace: false,
    });
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-5" data-component="bandeja-ordenes-compra">
      <PageHeader
        titulo="Órdenes de compra"
        acciones={
          canCrear && (
            <Button onClick={() => nuevaOC.abrir()} data-action="nueva-oc">
              <Plus className="mr-2 h-4 w-4" />
              Nueva OC
            </Button>
          )
        }
      />

      <ListadoModulo
        pie={
          <PaginacionTabla
            total={ordenesQuery.data?.totalCount ?? 0}
            offset={((search.page ?? 1) - 1) * (search.pageSize ?? 10)}
            limit={search.pageSize ?? 10}
            onChange={(offset, limit) =>
              setSearch({ ...search, page: Math.floor(offset / limit) + 1, pageSize: limit })
            }
          />
        }
        tabs={
          <TabsEstado
            opciones={Object.values(EstadoOrdenCompra)
              .filter((v): v is EstadoOrdenCompra => typeof v === 'number')
              .map((valor) => ({ valor, label: estadoOcToString(valor) }))}
            valor={search.estado}
            conteos={undefined}
            onChange={(estado) =>
              setSearch({ ...search, estado: estado as BandejaOcSearch['estado'], page: 1 })
            }
          />
        }
        filtros={<FiltrosBandejaOc search={search} onChange={setSearch} />}
      >
        <RenderTabla
          query={ordenesQuery}
          itemsFiltrados={itemsFiltrados}
          proveedoresMap={proveedoresMap}
          usuariosMap={usuariosMap}
          search={search}
        />
      </ListadoModulo>
    </div>
  );
}

interface RenderTablaProps {
  query: ReturnType<typeof useOrdenesCompra>;
  itemsFiltrados: readonly OrdenCompraResumen[];
  proveedoresMap: Map<string, { nombreComercial: string | null; razonSocial: string }>;
  usuariosMap: Map<string, { nombre: string }>;
  search: BandejaOcSearch;
}

type SortKey = 'folio' | 'fechaDocumento' | 'estado';

function RenderTabla({
  query,
  itemsFiltrados,
  proveedoresMap,
  usuariosMap,
  search,
}: RenderTablaProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const itemsOrdenados = useMemo(() => {
    if (sort == null) return itemsFiltrados;
    return [...itemsFiltrados].sort((a, b) => compareItemsBy(a, b, sort.key, sort.dir));
  }, [itemsFiltrados, sort]);

  if (query.isLoading) {
    return (
      <TableSkeleton
        rows={8}
        columns={[
          { width: 'w-32' },
          { width: 'w-24' },
          { width: 'w-48' },
          { width: 'w-40' },
          { width: 'w-24' },
          { width: 'w-32' },
          { width: 'w-12' },
        ]}
      />
    );
  }

  if (query.isError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => query.refetch()} />;
  }

  const total = query.data?.totalCount ?? 0;

  if (total === 0) {
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title="Aún no hay órdenes de compra que coincidan."
        description="Ajusta los filtros o cambia de preset. Cuando se creen, aparecerán aquí."
      />
    );
  }

  if (itemsFiltrados.length === 0 && search.q) {
    return (
      <EmptyState
        title={`Ningún folio coincide con "${search.q}".`}
        description="La búsqueda es sobre la página actual; cambia o limpia el filtro para volver a ver los resultados."
      />
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <Table className="w-full min-w-full table-fixed text-sm">
        <colgroup>
          <col className="w-[180px]" />
          <col className="w-[110px]" />
          <col />
          <col className="w-[150px]" />
          <col className="w-[230px]" />
          <col className="w-[84px]" />
          <col className="w-[76px]" />
        </colgroup>
        <TableHeader className="bg-surface-muted/50 text-xs uppercase tracking-wide text-ink-muted">
          <TableRow>
            <TableHead className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="folio"
                label="Folio"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaDocumento"
                label="Fecha"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="px-3 py-2 text-left font-medium">Proveedor</TableHead>
            <TableHead className="px-3 py-2 text-left font-medium">Comprador</TableHead>
            <TableHead className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="estado"
                label="Estado"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="px-3 py-2 text-left font-medium">
              <abbr title="Recepción · Facturación · Pago">Sub-estados</abbr>
            </TableHead>
            <TableHead className="px-3 py-2 text-right font-medium">Ver</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody className="[&_tr]:h-12">
          {itemsOrdenados.map((oc, i) => {
            const proveedor = proveedoresMap.get(oc.proveedorId);
            const proveedorLabel =
              proveedor != null
                ? (proveedor.nombreComercial ?? proveedor.razonSocial)
                : '—';
            const comprador =
              oc.compradorNombre ?? usuariosMap.get(oc.compradorTitularId)?.nombre ?? 'Sin asignar';
            return (
              <TableRow key={oc.id} className={i % 2 === 1 ? 'bg-surface-muted/20' : undefined}>
                <TableCell className="whitespace-nowrap px-3 py-2 font-mono">
                  <Link
                    className="font-mono text-xs font-medium text-brand"
                    to="/compras/ordenes/$id"
                    params={{ id: oc.id }}
                    state={{ bandejaOcSearch: search } as never}
                  >
                    {oc.folio}
                  </Link>
                </TableCell>
                <TableCell className="px-3 py-2">
                  <DateTimeDisplay value={oc.fechaDocumento} className="whitespace-nowrap" />
                </TableCell>
                <TableCell className="px-3 py-2 truncate" title={proveedorLabel}>
                  {proveedorLabel}
                </TableCell>
                <TableCell className={`px-3 py-2 truncate${comprador === 'Sin asignar' ? ' text-ink-muted' : ''}`} title={comprador}>
                  {comprador}
                </TableCell>
                <TableCell className="px-3 py-2">
                  <div className="flex flex-col items-start gap-1">
                    <EstadoBadge tipo="orden-compra" estado={oc.estado} />
                    {oc.estadoComercialProveedor != null &&
                      (oc.estadoComercialProveedor !== EstadoComercialProveedorOc.SinRespuesta ||
                        oc.estado === EstadoOrdenCompra.Autorizada) && (
                        <span className="text-xs text-ink-muted">
                          {ESTADO_COMERCIAL_PROVEEDOR_LABELS[oc.estadoComercialProveedor]}
                        </span>
                      )}
                  </div>
                </TableCell>
                <TableCell className="px-3 py-2">
                  <HoverSubEstados
                    recepcion={oc.subEstadoRecepcion}
                    facturacion={oc.subEstadoFacturacion}
                    pago={oc.subEstadoPago}
                  />
                </TableCell>
                <TableCell className="px-3 py-2 text-right">
                  <Button asChild variant="ghost" size="sm">
                    <Link
                      to="/compras/ordenes/$id"
                      params={{ id: oc.id }}
                      state={{ bandejaOcSearch: search } as never}
                    >
                      Ver
                    </Link>
                  </Button>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}

// ============================================================================
// Sub-estados compactos: PROMOVIDO a `<HoverSubEstados>` cross-vista en
// `components/HoverSubEstados.tsx` (UF6-PR1). UF7-PR1 lo consumirá
// también en la pantalla de Partidas Abiertas.
// ============================================================================
