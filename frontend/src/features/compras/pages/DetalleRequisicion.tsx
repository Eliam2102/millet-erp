import { useMemo, useState } from 'react';
import {
  Link,
  useLocation,
  useParams,
  useMatches,
} from '@tanstack/react-router';
import { History, Printer, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  CollaborationIndicator,
  EditandoBanner,
} from '@/components/erp';
import { TableSkeleton } from '@/components/erp/feedback/TableSkeleton';
import { useRequisicion } from '@/features/compras/api';
import { useHistoricoRequisicion } from '@/features/compras/api/useHistoricoRequisicion';
import {
  useSucursales,
  useAlmacenes,
  useArticulos,
  mapById,
} from '@/features/catalogos/api';
import { CabeceraRequisicion } from '@/features/compras/components/CabeceraRequisicion';
import { ListaLineas } from '@/features/compras/components/ListaLineas';
import { EditorLineas } from '@/features/compras/components/EditorLineas';
import { CubrimientoEstimadoPanel } from '@/features/compras/components/CubrimientoEstimadoPanel';
import { usePreviewCubrimiento } from '@/features/compras/api/usePreviewCubrimiento';
import { EstadoRequisicion } from '@/features/compras/api/types';
import { HistoricoModal } from '@/features/compras/components/HistoricoModal';
import { DetalleErrorBoundary } from '@/features/compras/components/DetalleErrorBoundary';
import { AccionesRequisicion } from '@/features/compras/components/AccionesRequisicion';
import { ResumenCubrimiento } from '@/features/compras/components/ResumenCubrimiento';
import { EstadoBadge } from '@/components/erp/display/EstadoBadge';
import {
  accionAgregarLinea,
  accionEditarLinea,
  accionEditarNotasLinea,
  accionEliminarLinea,
} from '@/features/compras/lib/acciones-disponibles';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  DEFAULT_BANDEJA_SEARCH,
  type BandejaSearch,
} from '@/features/compras/lib/bandeja-search-schema';
import {
  DEFAULT_PENDIENTES_SEARCH,
  type PendientesSearch,
} from '@/features/compras/lib/pendientes-search-schema';

/**
 * <c>P3 — Detalle de requisición</c> (read-only en UF1-PR2).
 *
 * <para>Lee el id de la URL (<c>$id</c>), carga el detalle con
 * <c>useRequisicion(id)</c> (que captura el ETag para mutations
 * futuras), y resuelve los IDs de cabecera/líneas/autorizaciones
 * contra los catálogos. Errores 403/404 se delegan al
 * <c>&lt;DetalleErrorBoundary/&gt;</c> con páginas dedicadas
 * (doc 05 §13.6).</para>
 *
 * <para><c>&lt;CollaborationIndicator/&gt;</c> arriba como stub
 * silente (UF0-PR2). UF8-PR1 lo activa con SignalR real.</para>
 *
 * <para><b>Preservación de filtros</b> (doc 05 §13.9): la bandeja
 * pasa <c>state.bandejaSearch</c> al <c>&lt;Link/&gt;</c> de "Ver";
 * acá lo leemos con <c>useLocation()</c>. Si el usuario llega al
 * detalle por URL directa o tras refresh (state vacío), caemos a
 * <c>DEFAULT_BANDEJA_SEARCH</c> y los CTAs vuelven a la bandeja sin
 * filtros — el browser back igual respeta el history nativo.</para>
 */
export function DetalleRequisicion() {
  // <c>strict: false</c> permite que el componente viva en dos rutas
  // (<c>/requisiciones/$id</c> y <c>/pendientes/$id</c>) sin acoplarse
  // al <c>from</c> de una específica.
  const { id } = useParams({ strict: false }) as { id: string };
  const location = useLocation();
  const matches = useMatches();
  const parent = inferirRutaPadre(matches, location.pathname);
  const bandejaSearch =
    extractBandejaSearch(location.state) ?? DEFAULT_BANDEJA_SEARCH;
  const pendientesSearch =
    extractPendientesSearch(location.state) ?? DEFAULT_PENDIENTES_SEARCH;
  const [historicoAbierto, setHistoricoAbierto] = useState(false);

  const requisicionQuery = useRequisicion(id);
  const historicoQuery = useHistoricoRequisicion(id);
  const sucursalesQuery = useSucursales();
  const almacenesQuery = useAlmacenes();

  const sucursalesMap = useMemo(
    () => mapById(sucursalesQuery.data?.items),
    [sucursalesQuery.data],
  );
  const almacenesMap = useMemo(
    () => mapById(almacenesQuery.data?.items),
    [almacenesQuery.data],
  );

  /**
   * Resuelve sucursal / almacén por id (catálogos chicos que el backend aún
   * no enriquece en el DTO). Requisitante, departamento (ADR-0042) y proveedor
   * sugerido (addendum) ya vienen resueltos en el DTO. Si no matchea, devuelve
   * el id raw (fallback del frontend: mostrar id ≠ crash).
   */
  const resolverNombre = useMemo(
    () => (id: string | null | undefined): string => {
      if (id == null) return '—';
      const s = sucursalesMap.get(id);
      if (s) return s.nombre;
      const a = almacenesMap.get(id);
      if (a) return a.nombre;
      return id;
    },
    [sucursalesMap, almacenesMap],
  );

  if (requisicionQuery.isError) {
    return (
      <DetalleErrorBoundary
        error={requisicionQuery.error}
        onRetry={() => requisicionQuery.refetch()}
        bandejaSearch={bandejaSearch}
      />
    );
  }

  if (requisicionQuery.isLoading || requisicionQuery.data == null) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Cargando…</h1>
        <TableSkeleton
          rows={5}
          columns={[
            { width: 'w-32' },
            { width: 'w-48' },
            { width: 'w-24' },
            { width: 'w-32' },
          ]}
        />
      </div>
    );
  }

  const rq = requisicionQuery.data;
  const historicoCount = historicoQuery.data?.length ?? 0;

  return (
    <div className="flex flex-col gap-4">
      {/* Sub-topbar — sticky arriba del panel detalle. Contiene el
          contexto de la RQ (folio + estado + collab) a la izquierda,
          las acciones de workflow al centro, y los utilities (Imprimir,
          Histórico, Cerrar) a la derecha. */}
      <header
        className="sticky top-14 z-10 flex flex-wrap items-center gap-3 border-b bg-background/95 px-4 py-2 backdrop-blur"
        data-print="hidden"
      >
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate font-mono text-sm font-semibold">
            {rq.folio}
          </span>
          <EstadoBadge
            tipo="requisicion"
            estado={rq.estado}
            situacion={rq.situacionSurtido}
          />
          <CollaborationIndicator entidad="requisicion" id={rq.id} />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <AccionesRequisicion rq={rq} />
        </div>

        <div className="ml-auto flex items-center gap-1">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => window.print()}
            aria-label="Imprimir"
          >
            <Printer className="mr-1.5 h-4 w-4" />
            Imprimir
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => setHistoricoAbierto(true)}
            disabled={historicoQuery.isLoading}
          >
            <History className="mr-1.5 h-4 w-4" />
            Histórico{historicoCount > 0 && ` (${historicoCount})`}
          </Button>
          <Button asChild variant="ghost" size="icon">
            {parent === 'pendientes' ? (
              <Link
                to="/compras/pendientes"
                search={pendientesSearch}
                aria-label="Cerrar"
              >
                <X className="h-4 w-4" />
              </Link>
            ) : (
              <Link
                to="/compras/requisiciones"
                search={bandejaSearch}
                aria-label="Cerrar"
              >
                <X className="h-4 w-4" />
              </Link>
            )}
          </Button>
        </div>
      </header>

      <div className="space-y-6 px-4 pb-6">
        <div data-print="hidden">
          <EditandoBanner entidad="requisicion" id={rq.id} />
        </div>

        <CabeceraRequisicion rq={rq} resolverNombre={resolverNombre} />

        <section className="space-y-2">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="text-lg font-semibold">Líneas</h2>
            <ResumenCubrimiento lineas={rq.lineas} estado={rq.estado} />
          </div>
          <RenderLineas rq={rq} />
        </section>
      </div>

      <HistoricoModal
        open={historicoAbierto}
        onOpenChange={setHistoricoAbierto}
        folio={rq.folio}
        entradas={historicoQuery.data ?? []}
        isLoading={historicoQuery.isLoading}
      />
    </div>
  );
}

/**
 * Decide entre <c>&lt;EditorLineas/&gt;</c> (con acciones según matriz
 * §6.1) y <c>&lt;ListaLineas/&gt;</c> (read-only puro). El editor se
 * usa siempre que haya AL MENOS UNA acción visible para el usuario;
 * si todas están ocultas (estado terminal o sin permisos), cae al
 * read-only para evitar tabla con columna "Acciones" vacía.
 */
function RenderLineas({ rq }: { rq: import('@/features/compras/api/types').RequisicionResponse }) {
  const permisos = useAuthStore((s) => s.permisos);
  const hayAlgunaAccion =
    accionAgregarLinea(rq, permisos).visible ||
    accionEditarLinea(rq, permisos).visible ||
    accionEliminarLinea(rq, permisos).visible ||
    accionEditarNotasLinea(rq, permisos).visible;

  // PR-C: preview del cubrimiento estimado, lazy — solo se consulta el stock
  // mientras la RQ está EnAutorizacion (antes de la bifurcación real, cuando
  // el CubrimientoBar real está en 0%). Va en un panel APARTE de la tabla de
  // líneas para no confundir el estimado con el cubrimiento reservado, y para
  // que aplique tanto si la tabla es EditorLineas como ListaLineas.
  const esEnAutorizacion = rq.estado === EstadoRequisicion.EnAutorizacion;
  const preview = usePreviewCubrimiento(rq.id, esEnAutorizacion);
  const mostrarPreview =
    esEnAutorizacion && (preview.isLoading || (preview.data?.aplica ?? false));

  // Catálogo de artículos para resolver articuloId → "clave — nombre"
  // en la versión read-only. EditorLineas carga su propio catálogo
  // internamente.
  const articulosQuery = useArticulos({
    limit: 1000,
    includeInactivas: true,
  });
  const articulosMap = useMemo(
    () => mapById(articulosQuery.data?.items),
    [articulosQuery.data],
  );
  const resolverArticulo = (articuloId: string): string => {
    const a = articulosMap.get(articuloId);
    return a ? `${a.clave} — ${a.nombre}` : articuloId;
  };

  return (
    <div className="space-y-3">
      {mostrarPreview && (
        <CubrimientoEstimadoPanel
          lineas={preview.data?.lineas ?? []}
          resolverArticulo={resolverArticulo}
          isLoading={preview.isLoading}
        />
      )}
      {hayAlgunaAccion ? (
        <EditorLineas rq={rq} />
      ) : (
        <ListaLineas lineas={rq.lineas} resolverArticulo={resolverArticulo} />
      )}
    </div>
  );
}

/**
 * Lee <c>bandejaSearch</c> del <c>state</c> del location si la bandeja
 * lo pasó al navegar (vía <c>&lt;Link state={...}/&gt;</c>). Devuelve
 * <c>undefined</c> si no hay state válido — el caller cae a
 * <c>DEFAULT_BANDEJA_SEARCH</c>.
 *
 * <para>El state de TanStack Router es <c>unknown</c> por defecto;
 * validamos manualmente para no acoplar tipos a la bandeja.</para>
 */
function extractBandejaSearch(state: unknown): BandejaSearch | undefined {
  if (state == null || typeof state !== 'object') return undefined;
  const candidate = (state as Record<string, unknown>).bandejaSearch;
  if (candidate == null || typeof candidate !== 'object') return undefined;
  // No re-validamos con Zod aquí (el state viene de nuestra propia
  // bandeja, no de URL externa). Cast deliberado — si el shape cambia
  // en el futuro, el caller verá un BandejaSearch parcial y caerá a
  // defaults via spread.
  return candidate as BandejaSearch;
}

function extractPendientesSearch(state: unknown): PendientesSearch | undefined {
  if (state == null || typeof state !== 'object') return undefined;
  const candidate = (state as Record<string, unknown>).pendientesSearch;
  if (candidate == null || typeof candidate !== 'object') return undefined;
  return candidate as PendientesSearch;
}

/**
 * Infiere a cuál bandeja debe volver el botón "Cerrar" (X) del detalle.
 * El componente <c>&lt;DetalleRequisicion/&gt;</c> está montado en dos
 * rutas (<c>/compras/requisiciones/$id</c> y
 * <c>/compras/pendientes/$id</c>); usamos los matches del router como
 * fuente primaria (más fiable que parsear pathname) y caemos a
 * pathname para casos de test/SSR donde matches puede no estar listo.
 */
function inferirRutaPadre(
  matches: ReturnType<typeof useMatches>,
  pathname: string,
): 'requisiciones' | 'pendientes' {
  const enPendientes =
    matches.some((m) => m.routeId.includes('/pendientes/')) ||
    pathname.startsWith('/compras/pendientes');
  return enPendientes ? 'pendientes' : 'requisiciones';
}
