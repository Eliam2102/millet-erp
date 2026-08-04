import { useMemo, type ReactNode } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { useOrdenesCompra } from '@/features/compras/ordenes/api/useOrdenesCompra';
import { useProveedores, useUsuarios } from '@/features/catalogos/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { FiltrosBandejaOc } from '@/features/compras/ordenes/components/FiltrosBandejaOc';
import { ListaOrdenesCompacta } from '@/features/compras/ordenes/components/ListaOrdenesCompacta';
import { useNuevaOrdenCompra } from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import {
  DEFAULT_BANDEJA_OC_SEARCH,
  type BandejaOcSearch,
} from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>P1 Bandeja general de Órdenes de Compra — master-detail layout</c>.
 * Mismo patrón cross-módulo que <c>&lt;RequisicionesLayout/&gt;</c>
 * (frontend/docs/patrones-compras.md §6.1):
 *
 * <list>
 *   <item><b>Lista compacta</b> (320px sticky a la izquierda) — folio
 *   + estado + fecha + referencia del proveedor. Highlight del item
 *   activo. Filtros + presets en la cabecera de la columna.</item>
 *   <item><b>Panel detalle</b> (resto del ancho) — recibe via
 *   <c>detalle</c> prop el contenido de la OC seleccionada, o un
 *   placeholder cuando <c>idActivo</c> es <c>null</c>.</item>
 * </list>
 *
 * <para>Mobile sub-md drill-down: con <c>idActivo</c> la lista se
 * oculta y el detalle ocupa toda la pantalla; sin id, lista
 * full-width.</para>
 *
 * <para>La ruta <c>/compras/ordenes</c> renderiza este layout con
 * <c>idActivo=null</c>. La ruta <c>/compras/ordenes/$id</c> lo
 * renderiza con <c>idActivo</c> = id de la URL y pasa
 * <c>&lt;DetalleOrdenCompra/&gt;</c> como <c>detalle</c>.</para>
 *
 * <para><b>Sin botón "Nueva OC" todavía</b>: el Sheet "Nueva OC" con
 * sus 3 modos (1:1 / consolidación / sin RQ previa) llega en UF2-PR1
 * con su provider <c>useNuevaOrdenCompra</c>. Mientras, el botón se
 * omite (UX más honesta que un click muerto) — alineado con la
 * convención del QuickCreateMenu en patrones-compras §6.4.</para>
 */
export interface OrdenesCompraLayoutProps {
  /** Id de la OC abierta en el panel; <c>null</c> = sin selección. */
  idActivo: string | null;
  /** Contenido del panel detalle. Si <c>idActivo</c> es null, este
   * prop se ignora y se muestra el placeholder. */
  detalle?: ReactNode;
}

export function OrdenesCompraLayout({
  idActivo,
  detalle,
}: OrdenesCompraLayoutProps) {
  // <c>useSearch({ strict: false })</c> permite que el componente
  // viva en dos rutas (<c>/ordenes</c> y <c>/ordenes/$id</c>) sin
  // acoplarse al <c>from</c> de una específica. Caveat: con
  // <c>strict: false</c> NO aplica el <c>validateSearch</c> Zod del
  // index (que tiene defaults page=1/pageSize=50), así que cuando el
  // master-detail arranca sin search en la URL los campos vienen
  // <c>undefined</c>. Mezclamos con <c>DEFAULT_BANDEJA_OC_SEARCH</c>
  // explícitamente para asegurar que page/pageSize siempre lleguen
  // al endpoint — antes el backend rechazaba la URL sin query string
  // con <c>BadHttpRequestException</c>. El backend también se hardenó
  // (defaults en la lambda), pero esta defensa-in-depth mantiene el
  // contrato simétrico entre los dos rutas.
  const rawSearch = useSearch({ strict: false }) as Partial<BandejaOcSearch>;
  const search: BandejaOcSearch = {
    ...DEFAULT_BANDEJA_OC_SEARCH,
    ...rawSearch,
  };
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

  // Mantener catálogos transversales en cache para el panel detalle
  // y para resolución de nombres en columnas de la lista. limit alto
  // + includeInactivas para no perder proveedores históricos.
  useProveedores({ limit: 1000, includeInactivas: true });
  useUsuarios();

  // El endpoint no acepta folio como filtro server-side hoy; el
  // <c>q</c> del topbar global se manda como <c>referenciaProveedor</c>
  // en <c>useOrdenesCompra</c> y no necesita doble filtrado client-side.
  // Cuando se agregue search por folio, hacerlo dentro de un
  // <c>useMemo</c> sobre <c>data?.items</c> directamente — sin
  // intermediar variable que cambie en cada render.
  const itemsFiltrados = useMemo(
    () => ordenesQuery.data?.items ?? [],
    [ordenesQuery.data],
  );

  function setSearch(next: BandejaOcSearch) {
    if (idActivo != null) {
      navigate({
        to: '/compras/ordenes/$id',
        params: { id: idActivo },
        search: next,
        replace: false,
      });
    } else {
      navigate({
        to: '/compras/ordenes',
        search: next,
        replace: false,
      });
    }
  }

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      {/* ── Master (lista) ────────────────────────────── */}
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de órdenes de compra"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">
            Bandeja de OCs
          </h1>
          {canCrear && (
            <Button
              size="sm"
              onClick={() => nuevaOC.abrir()}
              data-action="nueva-oc"
            >
              <Plus className="mr-1 h-4 w-4" />
              Nueva
            </Button>
          )}
        </div>

        <FiltrosBandejaOc search={search} onChange={setSearch} />

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderListaContent
            query={ordenesQuery}
            items={itemsFiltrados}
            idActivo={idActivo}
          />
        </div>
      </aside>

      {/* ── Detail panel ──────────────────────────────── */}
      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de orden de compra"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface RenderListaContentProps {
  query: ReturnType<typeof useOrdenesCompra>;
  items: readonly import('@/features/compras/ordenes/api/types').OrdenCompraResumen[];
  idActivo: string | null;
}

function RenderListaContent({
  query,
  items,
  idActivo,
}: RenderListaContentProps) {
  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton
          rows={8}
          columns={[{ width: 'w-full' }, { width: 'w-full' }]}
        />
      </div>
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

  // Necesitamos search para preservar filtros en el Link. La lista
  // los recibe como prop.
  return <ListaOrdenesCompactaConSearch items={items} idActivo={idActivo} />;
}

function ListaOrdenesCompactaConSearch({
  items,
  idActivo,
}: {
  items: readonly import('@/features/compras/ordenes/api/types').OrdenCompraResumen[];
  idActivo: string | null;
}) {
  // Lee el search del contexto del router para pasarlo al Link de
  // cada fila — preserva los filtros al navegar al detalle. Mismo
  // merge con DEFAULT_BANDEJA_OC_SEARCH que arriba para que page/
  // pageSize nunca lleguen <c>undefined</c>.
  const rawSearch = useSearch({ strict: false }) as Partial<BandejaOcSearch>;
  const search: BandejaOcSearch = {
    ...DEFAULT_BANDEJA_OC_SEARCH,
    ...rawSearch,
  };
  return (
    <ListaOrdenesCompacta items={items} idActivo={idActivo} search={search} />
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Inbox className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una orden de compra de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
