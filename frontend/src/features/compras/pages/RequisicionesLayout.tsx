import { useMemo, type ReactNode } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { useRequisiciones } from '@/features/compras/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { FiltrosBandeja } from '@/features/compras/components/FiltrosBandeja';
import { ListaRequisicionesCompacta } from '@/features/compras/components/ListaRequisicionesCompacta';
import { useNuevaRequisicion } from '@/features/compras/components/nueva-requisicion-context';
import type { BandejaSearch } from '@/features/compras/lib/bandeja-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>P1 Bandeja general de Requisiciones — master-detail layout</c>
 * (design/frontend-polish).
 *
 * <para>Reemplaza al ex <c>&lt;BandejaRequisiciones/&gt;</c> full-page.
 * En desktop muestra dos columnas:</para>
 *
 * <list>
 *   <item><b>Lista compacta</b> (320px sticky a la izquierda) — folio
 *   + estado + fecha + descripción truncada. Highlight del item
 *   activo. Filtros + sort en la cabecera de la columna.</item>
 *   <item><b>Panel de detalle</b> (resto del ancho) — recibe via
 *   <c>detalle</c> prop el contenido de la requisición seleccionada,
 *   o un placeholder cuando <c>idActivo</c> es <c>null</c>.</item>
 * </list>
 *
 * <para>En mobile sub-md aplica drill-down: cuando hay <c>idActivo</c>
 * la lista se oculta y el detalle ocupa toda la pantalla; cuando no
 * lo hay, la lista ocupa toda la pantalla. El back nativo del
 * navegador vuelve sin perder filtros (los preserva el state del
 * <c>&lt;Link/&gt;</c>).</para>
 *
 * <para>La ruta <c>/compras/requisiciones</c> renderiza este layout
 * con <c>idActivo=null</c>. La ruta <c>/compras/requisiciones/$id</c>
 * lo renderiza con <c>idActivo</c> = id de la URL y pasa
 * <c>&lt;DetalleRequisicion/&gt;</c> como <c>detalle</c>.</para>
 */
export interface RequisicionesLayoutProps {
  /** Id de la RQ abierta en el panel; <c>null</c> = sin selección. */
  idActivo: string | null;
  /** Contenido del panel detalle. Si <c>idActivo</c> es null, este
   * prop se ignora y se muestra el placeholder. */
  detalle?: ReactNode;
}

export function RequisicionesLayout({
  idActivo,
  detalle,
}: RequisicionesLayoutProps) {
  // <c>useSearch({ strict: false })</c> permite que este componente
  // viva en dos rutas (<c>/requisiciones</c> y <c>/requisiciones/$id</c>)
  // sin acoplarse al <c>from</c> de una específica. El cast a
  // <c>BandejaSearch</c> es seguro: ambas rutas comparten el schema
  // del index (la hija hereda).
  const search = useSearch({ strict: false }) as BandejaSearch;
  const navigate = useNavigate();
  const nuevaRequisicion = useNuevaRequisicion();

  const canCrear = useHasPermission(PermisosCanonicos.ComprasRequisicionesCrear);

  const requisicionesQuery = useRequisiciones({
    estado: search.estado,
    departamentoId: search.departamentoId,
    requisitanteId: search.requisitanteId,
    offset: search.offset,
    limit: search.limit,
  });

  const itemsFiltrados = useMemo(() => {
    const items = requisicionesQuery.data?.items ?? [];
    if (!search.q) return items;
    const needle = search.q.toLowerCase();
    return items.filter((r) => r.folio.toLowerCase().includes(needle));
  }, [requisicionesQuery.data, search.q]);

  function setSearch(next: BandejaSearch) {
    // Mantener la ruta actual: si estamos en master-detail con un id
    // activo, filtrar NO debe regresar a la bandeja tabular — solo
    // refresca la lista de la izquierda. Sin id activo, navegamos al
    // index normal.
    if (idActivo != null) {
      navigate({
        to: '/compras/requisiciones/$id',
        params: { id: idActivo },
        search: next,
        replace: false,
      });
    } else {
      navigate({
        to: '/compras/requisiciones',
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
          // Mobile drill-down: cuando hay un id activo, ocultamos la
          // lista. Sin id, lista full-width.
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de requisiciones"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">
            Bandeja de requisiciones
          </h1>
          {canCrear && (
            <Button size="sm" onClick={() => nuevaRequisicion.abrir()}>
              <Plus className="mr-1 h-4 w-4" />
              Nueva
            </Button>
          )}
        </div>

        <FiltrosBandeja search={search} onChange={setSearch} />

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderListaContent
            query={requisicionesQuery}
            items={itemsFiltrados}
            idActivo={idActivo}
            search={search}
            canCrear={canCrear}
            onAbrirNueva={() => nuevaRequisicion.abrir()}
          />
        </div>
      </aside>

      {/* ── Detail panel ──────────────────────────────── */}
      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          // Mobile: sin id activo, ocultamos el panel para que la
          // lista ocupe la pantalla.
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de requisición"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface RenderListaContentProps {
  query: ReturnType<typeof useRequisiciones>;
  items: readonly import('@/features/compras/api/types').RequisicionListItemResponse[];
  idActivo: string | null;
  search: BandejaSearch;
  canCrear: boolean;
  onAbrirNueva: () => void;
}

function RenderListaContent({
  query,
  items,
  idActivo,
  search,
  canCrear,
  onAbrirNueva,
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

  const total = query.data?.total ?? 0;

  if (total === 0) {
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title="Aún no tienes requisiciones."
        description={
          canCrear
            ? 'Crea la primera para empezar.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        action={
          canCrear ? (
            <Button size="sm" onClick={onAbrirNueva}>
              <Plus className="mr-1 h-4 w-4" />
              Nueva requisición
            </Button>
          ) : undefined
        }
      />
    );
  }

  if (items.length === 0 && search.q) {
    return (
      <EmptyState
        title={`Ningún folio coincide con "${search.q}".`}
        description="Cambia o limpia el filtro de búsqueda del topbar."
      />
    );
  }

  return (
    <ListaRequisicionesCompacta
      items={items}
      idActivo={idActivo}
      search={search}
    />
  );
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Inbox className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una requisición de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
