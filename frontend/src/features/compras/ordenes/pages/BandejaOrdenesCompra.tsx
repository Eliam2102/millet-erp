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
} from '@/components/erp';
import { useOrdenesCompra } from '@/features/compras/ordenes/api/useOrdenesCompra';
import { useProveedores, useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { FiltrosBandejaOc } from '@/features/compras/ordenes/components/FiltrosBandejaOc';
import { HoverSubEstados } from '@/features/compras/ordenes/components/HoverSubEstados';
import { useNuevaOrdenCompra } from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import { type OrdenCompraResumen } from '@/features/compras/ordenes/api/types';
import { type BandejaOcSearch } from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';

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
  const usuariosMap = useMemo(
    () => mapById(usuariosQuery.data?.items),
    [usuariosQuery.data],
  );

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
    <div className="space-y-4 p-4" data-component="bandeja-ordenes-compra">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          Bandeja de OCs
        </h1>
        {canCrear && (
          <Button onClick={() => nuevaOC.abrir()} data-action="nueva-oc">
            <Plus className="mr-2 h-4 w-4" />
            Nueva OC
          </Button>
        )}
      </div>

      <FiltrosBandejaOc search={search} onChange={setSearch} />

      <RenderTabla
        query={ordenesQuery}
        itemsFiltrados={itemsFiltrados}
        proveedoresMap={proveedoresMap}
        usuariosMap={usuariosMap}
        search={search}
        onChangeSearch={setSearch}
      />
    </div>
  );
}

interface RenderTablaProps {
  query: ReturnType<typeof useOrdenesCompra>;
  itemsFiltrados: readonly OrdenCompraResumen[];
  proveedoresMap: Map<
    string,
    { nombreComercial: string | null; razonSocial: string }
  >;
  usuariosMap: Map<string, { nombre: string }>;
  search: BandejaOcSearch;
  onChangeSearch: (next: BandejaOcSearch) => void;
}

type SortKey = 'folio' | 'fechaDocumento' | 'estado';

function RenderTabla({
  query,
  itemsFiltrados,
  proveedoresMap,
  usuariosMap,
  search,
  onChangeSearch,
}: RenderTablaProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const itemsOrdenados = useMemo(() => {
    if (sort == null) return itemsFiltrados;
    return [...itemsFiltrados].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
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
    <div className="overflow-x-auto rounded-md border bg-card">
      <table className="w-full min-w-[820px] text-sm">
        <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
          <tr>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="folio"
                label="Folio"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaDocumento"
                label="Fecha"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left font-medium">Proveedor</th>
            <th className="px-3 py-2 text-left font-medium">Comprador</th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="estado"
                label="Estado"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left font-medium">
              <abbr title="Recepción · Facturación · Pago">Sub-estados</abbr>
            </th>
            <th className="px-3 py-2 text-right font-medium">Ver</th>
          </tr>
        </thead>
        <tbody>
          {itemsOrdenados.map((oc, i) => {
            const proveedor = proveedoresMap.get(oc.proveedorId);
            const proveedorLabel =
              proveedor != null
                ? (proveedor.nombreComercial ?? proveedor.razonSocial)
                : oc.proveedorId;
            const comprador =
              usuariosMap.get(oc.compradorTitularId)?.nombre ??
              oc.compradorTitularId;
            return (
              <tr
                key={oc.id}
                className={i % 2 === 1 ? 'bg-muted/20' : undefined}
              >
                <td className="px-3 py-2 font-mono">{oc.folio}</td>
                <td className="px-3 py-2">
                  <DateTimeDisplay value={oc.fechaDocumento} />
                </td>
                <td className="px-3 py-2 truncate" title={proveedorLabel}>
                  {proveedorLabel}
                </td>
                <td className="px-3 py-2 truncate" title={comprador}>
                  {comprador}
                </td>
                <td className="px-3 py-2">
                  <EstadoBadge tipo="orden-compra" estado={oc.estado} />
                </td>
                <td className="px-3 py-2">
                  <HoverSubEstados
                    recepcion={oc.subEstadoRecepcion}
                    facturacion={oc.subEstadoFacturacion}
                    pago={oc.subEstadoPago}
                  />
                </td>
                <td className="px-3 py-2 text-right">
                  <Button asChild variant="ghost" size="sm">
                    <Link
                      to="/compras/ordenes/$id"
                      params={{ id: oc.id }}
                      state={{ bandejaOcSearch: search } as never}
                    >
                      Ver
                    </Link>
                  </Button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      <Paginacion
        total={total}
        page={search.page ?? 1}
        pageSize={search.pageSize ?? 50}
        onChange={(page, pageSize) =>
          onChangeSearch({ ...search, page, pageSize })
        }
      />
    </div>
  );
}

// ============================================================================
// Sub-estados compactos: PROMOVIDO a `<HoverSubEstados>` cross-vista en
// `components/HoverSubEstados.tsx` (UF6-PR1). UF7-PR1 lo consumirá
// también en la pantalla de Partidas Abiertas.
// ============================================================================

// ============================================================================
// Paginación page/pageSize (mirror del backend ListarOrdenesCompraResponse).
// Diferente al de RQ que usa offset/limit; cada submódulo respeta el shape
// real de su endpoint.
// ============================================================================

interface PaginacionProps {
  total: number;
  page: number;
  pageSize: number;
  onChange: (page: number, pageSize: number) => void;
}

function Paginacion({ total, page, pageSize, onChange }: PaginacionProps) {
  const desde = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const hasta = Math.min(page * pageSize, total);
  const tieneAnterior = page > 1;
  const tieneSiguiente = page * pageSize < total;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t bg-muted/30 px-3 py-2 text-xs text-muted-foreground">
      <span>
        Mostrando {desde}–{hasta} de {total}
      </span>
      <div className="flex items-center gap-2">
        <label className="flex items-center gap-1.5">
          Tamaño página:
          <select
            value={pageSize}
            onChange={(e) => onChange(1, Number(e.target.value))}
            className="rounded border bg-background px-1.5 py-0.5 text-xs"
            aria-label="Tamaño de página"
          >
            <option value={10}>10</option>
            <option value={50}>50</option>
            <option value={100}>100</option>
            <option value={200}>200</option>
          </select>
        </label>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!tieneAnterior}
          onClick={() => onChange(Math.max(1, page - 1), pageSize)}
        >
          Anterior
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!tieneSiguiente}
          onClick={() => onChange(page + 1, pageSize)}
        >
          Siguiente
        </Button>
      </div>
    </div>
  );
}
