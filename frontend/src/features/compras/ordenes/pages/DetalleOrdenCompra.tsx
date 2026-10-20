import { PageHeader } from '@/components/erp/PageHeader';
import { RecurrenteBadge } from '@/features/compras/components/IndicadoresPendiente';
import { CancelacionOcPanel } from '../components/CancelacionOcPanel';
import { RespuestaProveedorPanel } from '../components/RespuestaProveedorPanel';
import { useMemo, useState, type ReactNode } from 'react';
import { Link, useLocation, useParams } from '@tanstack/react-router';
import { Compass, GitBranch, Inbox, Lock, Printer, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  CollaborationIndicator,
  EditandoBanner,
  EmptyState,
  EstadoBadge,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import {
  useSucursales,
  useAlmacenes,
  useUsuarios,
  useCondicionesPago,
  useUsosPrincipales,
  mapById,
} from '@/features/catalogos/api';
import { useOrdenCompra } from '@/features/compras/ordenes/api/useOrdenCompra';
import { AccionesOC } from '@/features/compras/ordenes/components/AccionesOC';
import { AdjuntosManagerOc } from '@/features/compras/ordenes/components/AdjuntosManagerOc';
import { RevisionPreviaOc } from '@/features/compras/ordenes/components/RevisionPreviaOc';
import { AsideListOcsHermanas } from '@/features/compras/ordenes/components/AsideListOcsHermanas';
import { EditorLineas } from '@/features/compras/ordenes/components/EditorLineas';
import { StepperAutorizacionOc } from '@/features/compras/ordenes/components/StepperAutorizacionOc';
import { HistorialFirmasOc } from '@/features/compras/ordenes/components/HistorialFirmasOc';
import { SubEstadosBar } from '@/features/compras/ordenes/components/SubEstadosBar';
import { TabInformacion } from '@/features/compras/ordenes/components/TabInformacion';
import { TabPdf } from '@/features/compras/ordenes/components/TabPdf';
import { TimelineOrdenCompra } from '@/features/compras/ordenes/components/TimelineOrdenCompra';
import {
  DEFAULT_BANDEJA_OC_SEARCH,
  type BandejaOcSearch,
} from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';
import { calcularRevisionPrevia } from '@/features/compras/ordenes/lib/revision-previa-oc';
import { accionTransmitirAAutorizacion } from '@/features/compras/ordenes/lib/acciones-disponibles';
import { useTiposDocumentoOc } from '@/features/catalogos/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { esApiError, type ApiError } from '@/lib/api';

/**
 * <c>P3 — Detalle de orden de compra</c> (read-only en UF1-PR2).
 *
 * <para>Lee el id de la URL (<c>$id</c>), carga el detalle con
 * <c>useOrdenCompra(id)</c> (que captura el ETag para mutations
 * futuras), y resuelve los IDs de cabecera contra los catálogos
 * (proveedor, sucursal, almacén, usuarios). Errores 403/404 caen a
 * páginas dedicadas inline; 5xx a <c>&lt;ErrorState/&gt;</c>.</para>
 *
 * <para><b>Estructura</b> (mismo patrón que
 * <c>&lt;DetalleRequisicion/&gt;</c>):</para>
 *
 * <list>
 *   <item><b>Sub-topbar sticky</b>: folio + EstadoBadge + collaboration
 *   indicator + (botón "Imprimir") + (botón "Cerrar X" → vuelve a
 *   <c>/compras/ordenes</c> preservando filtros via state).</item>
 *   <item><b>SubEstadosBar</b> arriba: 3 barras Recepción /
 *   Facturación / Pago.</item>
 *   <item><b>StepperAutorizacionOc</b>: stepper de 4 pasos derivado
 *   del <c>estado</c>.</item>
 *   <item><b>Tabs</b>: Información (real, cabecera completa) /
 *   Líneas / Adjuntos / Autorización / Historial. Los 4 últimos son
 *   stubs que apuntan al PR donde se cablearán cuando el backend
 *   exponga los responses extendidos.</item>
 * </list>
 *
 * <para><b>Sin acciones de workflow todavía</b>: Transmitir / Aprobar /
 * Rechazar / Cancelar / Duplicar llegan en UF4 / UF5. Sin
 * <c>&lt;AccionesOC/&gt;</c> en este PR.</para>
 */
export function DetalleOrdenCompra() {
  // <c>strict: false</c> permite que el componente viva en
  // <c>/compras/ordenes/$id</c> sin acoplarse al <c>from</c> de la
  // ruta específica (consistente con <c>&lt;DetalleRequisicion/&gt;</c>).
  const { id } = useParams({ strict: false }) as { id: string };
  const location = useLocation();
  const bandejaSearch = extractBandejaOcSearch(location.state) ?? DEFAULT_BANDEJA_OC_SEARCH;

  const ocQuery = useOrdenCompra(id);
  const [tabActivo, setTabActivo] = useState<TabId>('informacion');
  const permisos = useAuthStore((s) => s.permisos);
  const tiposDocQuery = useTiposDocumentoOc();

  // Catálogos cargados con limit alto + includeInactivas para resolver
  // ids históricos en detalles (un proveedor/condición/uso pudo haberse
  // marcado inactivo después de usarse en esta OC). Sin esto, el ID
  // crudo aparece en pantalla.
  const sucursalesQuery = useSucursales();
  const almacenesQuery = useAlmacenes();
  const usuariosQuery = useUsuarios();
  const condicionesPagoQuery = useCondicionesPago({ includeInactivas: true });
  const usosPrincipalesQuery = useUsosPrincipales({ includeInactivas: true });

  const sucursalesMap = useMemo(() => mapById(sucursalesQuery.data?.items), [sucursalesQuery.data]);
  const almacenesMap = useMemo(() => mapById(almacenesQuery.data?.items), [almacenesQuery.data]);
  const usuariosMap = useMemo(() => mapById(usuariosQuery.data?.items), [usuariosQuery.data]);
  const condicionesPagoMap = useMemo(
    () => mapById(condicionesPagoQuery.data),
    [condicionesPagoQuery.data],
  );
  const usosPrincipalesMap = useMemo(
    () => mapById(usosPrincipalesQuery.data),
    [usosPrincipalesQuery.data],
  );

  /**
   * Resolver de id genérico contra los 6 catálogos cargados (usuarios,
   * proveedores, sucursales, almacenes, condiciones de pago, usos
   * principales). Si no matchea, muestra «—»: nunca el id crudo (un UUID
   * no es dato para el usuario).
   */
  const resolverNombre = useMemo(
    () =>
      (id: string | null | undefined): string => {
        if (id == null) return '—';
        const u = usuariosMap.get(id);
        if (u) return u.nombre;
        // Proveedor ya viene resuelto en el DTO (ADR-0042 addendum); no se
        // resuelve aquí. Cabecera usa oc.proveedorRazonSocial/Clave.
        const s = sucursalesMap.get(id);
        if (s) return s.nombre;
        const a = almacenesMap.get(id);
        if (a) return a.nombre;
        const cp = condicionesPagoMap.get(id);
        if (cp) return cp.nombre;
        const up = usosPrincipalesMap.get(id);
        if (up) return up.nombre;
        return '—';
      },
    [usuariosMap, sucursalesMap, almacenesMap, condicionesPagoMap, usosPrincipalesMap],
  );

  if (ocQuery.isError) {
    return (
      <DetalleErrorBoundaryOc
        error={ocQuery.error}
        onRetry={() => ocQuery.refetch()}
        bandejaSearch={bandejaSearch}
      />
    );
  }

  if (ocQuery.isLoading || ocQuery.data == null) {
    return (
      <div className="flex flex-col gap-5" aria-busy="true">
        <header className="flex items-center gap-3 border-b px-4 py-2">
          <Skeleton className="h-7 w-32" />
          <Skeleton className="h-5 w-24 rounded-full" />
          <Skeleton className="ml-4 h-8 w-28" />
        </header>
        <TableSkeleton
          rows={5}
          columns={[{ width: 'w-32' }, { width: 'w-48' }, { width: 'w-24' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const oc = ocQuery.data;
  // Revisión previa de «Transmitir»: solo cuando la acción aplica (Borrador/Rechazada + permiso).
  const revision = accionTransmitirAAutorizacion(oc, permisos).visible
    ? calcularRevisionPrevia(oc, tiposDocQuery.data)
    : undefined;

  return (
    <div className="flex flex-col gap-5">
      {/* ── Sub-topbar sticky ───────────────────────────── */}
      <header
        className="sticky top-14 z-10 md:top-0 flex flex-wrap items-center gap-3 border-b bg-surface-card py-2"
        data-print="hidden"
      >
        <div className="flex min-w-0 items-center gap-2">
          <PageHeader
            mono
            titulo={oc.folio}
            descripcion={oc.proveedorRazonSocial ?? oc.proveedorClave ?? '[PROVEEDOR]'}
          />
          <EstadoBadge tipo="orden-compra" estado={oc.estado} />
          <RecurrenteBadge esRecurrente={'esRecurrente' in oc && oc.esRecurrente === true} />
          <CollaborationIndicator entidad="orden-compra" id={oc.id} />
        </div>

        {/* Acciones contextuales de workflow inline en el header
            (mismo patrón que <DetalleRequisicion/>). Gateadas por la
            matriz §6.1; el componente se auto-oculta si ninguna acción
            aplica al estado + permisos. */}
        <div className="flex flex-wrap items-center gap-2">
          <AccionesOC oc={oc} revision={revision} />
        </div>

        <div className="ml-auto flex items-center gap-1">
          <Button asChild variant="ghost" size="sm">
            <Link
              to="/compras/trazabilidad/oc/$id"
              params={{ id: oc.id }}
              aria-label="Ver árbol de trazabilidad"
              data-action="ver-trazabilidad"
            >
              <GitBranch className="mr-1.5 h-4 w-4" />
              Trazabilidad
            </Link>
          </Button>
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
          <Button asChild variant="ghost" size="icon">
            <Link to="/compras/ordenes" search={bandejaSearch} aria-label="Cerrar">
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </header>

      {/* ── Cuerpo ──────────────────────────────────────── */}
      <div className="space-y-5">
        <div data-print="hidden">
          <EditandoBanner entidad="orden-compra" id={oc.id} />
        </div>

        <SubEstadosBar
          recepcion={oc.subEstadoRecepcion}
          facturacion={oc.subEstadoFacturacion}
          pago={oc.subEstadoPago}
        />

        <StepperAutorizacionOc estado={oc.estado} />
        <CancelacionOcPanel oc={oc} />
        <RespuestaProveedorPanel oc={oc} />

        {/* UF5-PR2: aside de la cadena de duplicación. Solo aparece
            si la OC actual es duplicada (tiene oc_origen_id) o es
            origen de duplicaciones. */}
        <div data-print="hidden">
          <AsideListOcsHermanas oc={oc} />
        </div>

        {revision && <RevisionPreviaOc revision={revision} onIr={setTabActivo} />}

        <TabsOc
          activo={tabActivo}
          onActivoChange={setTabActivo}
          informacion={<TabInformacion oc={oc} resolverNombre={resolverNombre} />}
          lineas={<EditorLineas oc={oc} />}
          adjuntos={<AdjuntosManagerOc oc={oc} />}
          pdf={<TabPdf oc={oc} />}
          autorizacion={
            <>
              <HistorialFirmasOc oc={oc} resolverNombre={resolverNombre} />
              <TimelineOrdenCompra
                ocId={oc.id}
                filtroEntidad="AutorizacionOC"
                emptyTitle="Sin eventos de autorización"
                emptyDescription="Cuando esta OC sea aprobada o rechazada en N1 o N2, los eventos individuales aparecerán aquí (con fecha, autorizador y notas por nivel)."
              />
            </>
          }
          historial={<TimelineOrdenCompra ocId={oc.id} />}
        />
      </div>
    </div>
  );
}

// ============================================================================
// Tabs (components/ui/tabs, UX-F1-09). Historia: minimal hasta que UF2-PR3 / UF3-PR2 / UF4-PR1 / UF7-PR3
// traigan el contenido real. No introducimos Radix Tabs (dependency
// nueva) para una UI tan simple; si en el futuro otros módulos
// necesitan tabs, promovemos a `components/erp/Tabs.tsx`.
// ============================================================================

const TAB_IDS = ['informacion', 'lineas', 'adjuntos', 'pdf', 'autorizacion', 'historial'] as const;
type TabId = (typeof TAB_IDS)[number];
const TAB_LABEL: Record<TabId, string> = {
  informacion: 'Información',
  lineas: 'Partidas',
  adjuntos: 'Adjuntos',
  pdf: 'PDF',
  autorizacion: 'Autorización',
  historial: 'Historial',
};

function TabsOc({
  activo,
  onActivoChange,
  informacion,
  lineas,
  adjuntos,
  pdf,
  autorizacion,
  historial,
}: {
  activo: TabId;
  onActivoChange: (tab: TabId) => void;
  informacion: ReactNode;
  /** Tab "Líneas" — wireado con <c>&lt;EditorLineas/&gt;</c> desde
   * UF2-PR3-b. Si <c>null</c>, cae al stub "próximamente". */
  lineas?: ReactNode;
  /** Tab "Adjuntos" — wireado con <c>&lt;AdjuntosManagerOc/&gt;</c>
   * desde UF3-PR2. Si <c>null</c>, cae al stub. */
  adjuntos?: ReactNode;
  /** Tab "PDF" — wireado con <c>&lt;TabPdf/&gt;</c> desde UF6-PR1.
   * Si <c>null</c>, cae al stub. */
  pdf?: ReactNode;
  /** Tab "Autorización" — wireado con
   * <c>&lt;TimelineOrdenCompra filtroEntidad="AutorizacionOC"/&gt;</c>
   * desde UF7-PR3. Si <c>null</c>, cae al stub. */
  autorizacion?: ReactNode;
  /** Tab "Historial" — wireado con <c>&lt;TimelineOrdenCompra/&gt;</c>
   * desde UF7-PR3. Si <c>null</c>, cae al stub. */
  historial?: ReactNode;
}) {
  const stub = (slot: ReactNode, nombre: string, titulo: string) =>
    slot ?? (
      <StubProximamente
        titulo={`${titulo} — wireup pendiente`}
        descripcion={`El caller no proporcionó el slot ${nombre}; verifica el wireup en DetalleOrdenCompra.`}
      />
    );
  const contenido: Record<TabId, ReactNode> = {
    informacion: informacion,
    lineas: stub(lineas, 'lineas', 'Partidas'),
    adjuntos: stub(adjuntos, 'adjuntos', 'Adjuntos'),
    pdf: stub(pdf, 'pdf', 'PDF'),
    autorizacion: stub(autorizacion, 'autorizacion', 'Autorización'),
    historial: stub(historial, 'historial', 'Historial'),
  };
  // Pestañas con formularios en edición: forceMount para no perder lo capturado
  // al cambiar de pestaña (el CSS de TabsContent oculta las inactivas).
  const FORCE_MOUNT: TabId[] = ['informacion', 'lineas'];

  return (
    <Tabs value={activo} onValueChange={(v) => onActivoChange(v as TabId)} data-component="tabs-oc">
      <TabsList aria-label="Secciones del detalle de OC">
        {TAB_IDS.map((id) => (
          <TabsTrigger
            key={id}
            value={id}
            id={`tab-${id}`}
            data-tab={id}
            data-activo={activo === id || undefined}
          >
            {TAB_LABEL[id]}
          </TabsTrigger>
        ))}
      </TabsList>

      {TAB_IDS.map((id) => (
        <TabsContent
          key={id}
          value={id}
          forceMount={FORCE_MOUNT.includes(id) ? true : undefined}
          className="pt-4 mt-0"
          data-tab-panel={id}
        >
          {contenido[id]}
        </TabsContent>
      ))}
    </Tabs>
  );
}

function StubProximamente({ titulo, descripcion }: { titulo: string; descripcion: string }) {
  return (
    <EmptyState icon={<Inbox className="h-10 w-10" />} title={titulo} description={descripcion} />
  );
}

// ============================================================================
// Error boundary local — 403 / 404 / 5xx con UX de página completa.
// Versión OC-específica del DetalleErrorBoundary de RQ; cuando 2+
// submódulos lo necesiten (CxP, etc.) se promueve a `components/erp/`
// con props parametrizadas (`bandejaPath`, `bandejaSearch`).
// ============================================================================

interface DetalleErrorBoundaryOcProps {
  error: unknown;
  onRetry?: () => void;
  bandejaSearch: BandejaOcSearch;
}

function DetalleErrorBoundaryOc({ error, onRetry, bandejaSearch }: DetalleErrorBoundaryOcProps) {
  if (esApiError(error)) {
    if (error.status === 403) {
      return <Page403 error={error} bandejaSearch={bandejaSearch} />;
    }
    if (error.status === 404) {
      return <Page404 bandejaSearch={bandejaSearch} />;
    }
  }

  const problem = esApiError(error) ? error.problem : undefined;
  return (
    <div className="space-y-4 p-4">
      <ErrorState problem={problem} onRetry={onRetry} />
    </div>
  );
}

function Page403({ error, bandejaSearch }: { error: ApiError; bandejaSearch: BandejaOcSearch }) {
  // Loguear traceId — si el 403 llega es por bug de gating de UI
  // (el sidebar no debería haber mostrado el item sin
  // <c>compras.ordenes.leer</c>).
  if (error.traceId != null) {
    console.warn(`[OC] 403 en GET /api/v1/compras/ordenes/{id} — traceId=${error.traceId}`);
  }
  const description =
    error.problem?.title && error.problem.title !== 'Forbidden'
      ? error.problem.title
      : 'Si crees que debería ser distinto, contacta a tu administrador.';

  return (
    <EmptyState
      icon={<Lock className="h-10 w-10" />}
      title="No tienes permiso para ver esta orden de compra."
      description={description}
      action={
        <Button asChild variant="outline" size="sm">
          <Link to="/compras/ordenes" search={bandejaSearch}>
            Volver a la bandeja
          </Link>
        </Button>
      }
    />
  );
}

function Page404({ bandejaSearch }: { bandejaSearch: BandejaOcSearch }) {
  return (
    <EmptyState
      icon={<Compass className="h-10 w-10" />}
      title="Orden de compra no encontrada."
      description="El folio puede haber sido removido o no pertenece a tu empresa actual."
      action={
        <Button asChild variant="outline" size="sm">
          <Link to="/compras/ordenes" search={bandejaSearch}>
            Volver a la bandeja
          </Link>
        </Button>
      }
    />
  );
}

// ============================================================================
// Helpers
// ============================================================================

interface BandejaOcSearchState {
  bandejaOcSearch: BandejaOcSearch;
}

function extractBandejaOcSearch(state: unknown): BandejaOcSearch | undefined {
  if (state != null && typeof state === 'object' && 'bandejaOcSearch' in state) {
    return (state as BandejaOcSearchState).bandejaOcSearch;
  }
  return undefined;
}
