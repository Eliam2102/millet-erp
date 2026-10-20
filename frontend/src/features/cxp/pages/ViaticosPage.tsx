import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { AlertTriangle, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  BarraFiltros,
  TabsEstado,
  useConteosPorEstado,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  TableSkeleton,
  UsuarioSelector,
} from '@/components/erp';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { cxpKeys } from '@/features/cxp/api/keys';
import { useViaticos } from '@/features/cxp/api/useViaticos';
import {
  EstadoSolicitudViaticos,
  type SolicitudViaticosListItem,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { AccionesViatico } from '@/features/cxp/components/AccionesViatico';
import { esApiError } from '@/lib/api';
import { EstadoSolicitudViaticosChip } from '@/features/cxp/components/EstadoChips';
import { NuevaSolicitudViaticosSheet } from '@/features/cxp/components/NuevaSolicitudViaticosSheet';
import { CapturarComprobacionViaticosSheet } from '@/features/cxp/components/CapturarComprobacionViaticosSheet';
import type { ViaticosSearch } from '@/features/cxp/lib/viaticos-search-schema';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

const FROM = '/_app/cxp/viaticos/' as const;

/**
 * <c>P1 — Bandeja de Viáticos Electrónicos</c> (doc 07 §FE-F5-PR1).
 * Vista única filtrable por empleado/jefe/estado. Acciones inline
 * contextuales según estado + permiso:
 * <list>
 *   <item><b>Solicitada</b>: jefe autoriza (puede escalar a DF) o rechaza.</item>
 *   <item><b>RequiereDireccionFinanzas</b>: DF autoriza o rechaza.</item>
 *   <item><b>AutorizadaPorJefe/AutorizadaCompleta</b>: Tesorería marca
 *         como pagado (proxy desde CxP).</item>
 *   <item><b>Anticipada</b>: empleado captura comprobación al regreso.</item>
 *   <item><b>ComprobacionCapturada</b>: CxP libera y calcula diferencia.</item>
 * </list>
 *
 * <para>PLATFORM-TODO(&lt;RutasPorRolViaticos&gt;): el spec menciona
 * 3 rutas distintas (mis-solicitudes/por-aprobar/por-revisar). Hoy hay
 * una sola página con filtros de URL para evitar duplicación. Si el
 * negocio quiere shortcuts en el sidebar, agregar cards con search
 * pre-poblada a <c>nav.ts</c>.</para>
 */
export function ViaticosPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeSolicitar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarViaticosSolicitar,
  );
  const [nuevaAbierta, setNuevaAbierta] = useState(false);
  const [comprobarTarget, setComprobarTarget] =
    useState<SolicitudViaticosListItem | null>(null);

  const query = useViaticos({
    estado: search.estado,
    empleadoId: search.empleadoId,
    jefeDirectoId: search.jefeDirectoId,
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  const conteos = useConteosPorEstado(
    cxpKeys.viaticos(),
    '/api/v1/cuentas-por-pagar/viaticos',
    { empleadoId: search.empleadoId, jefeDirectoId: search.jefeDirectoId, q: search.q },
  );

  function actualizarSearch(parcial: Partial<ViaticosSearch>) {
    navigate({
      to: '/cxp/viaticos',
      // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
      search: { ...search, page: undefined, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Viáticos"
        descripcion="Solicitud, doble autorización (jefe + DF si excede política), anticipo, comprobación al regreso y liquidación."
        acciones={
          <>
            {puedeSolicitar && (
              <Button onClick={() => setNuevaAbierta(true)}>
                <Plus className="mr-2 size-3.5" />
                Solicitar viáticos
              </Button>
            )}
          </>
        }
      />

      <TabsEstado
        opciones={TABS_ESTADO}
        valor={search.estado}
        onChange={(estado) => actualizarSearch({ estado: estado as EstadoSolicitudViaticos | undefined })}
        conteos={conteos.data}
      />

      <Filtros search={search} total={query.data?.total} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las solicitudes"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin solicitudes"
          description={
            search.estado != null || search.empleadoId || search.jefeDirectoId || search.q
              ? 'No hay solicitudes con los filtros aplicados.'
              : 'Aún no hay solicitudes de viáticos. Crea la primera desde «Solicitar viáticos».'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <Tabla items={items} onComprobar={setComprobarTarget} />
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
      )}

      <NuevaSolicitudViaticosSheet
        open={nuevaAbierta}
        onOpenChange={setNuevaAbierta}
      />
      <CapturarComprobacionViaticosSheet
        open={comprobarTarget !== null}
        onOpenChange={(open) => !open && setComprobarTarget(null)}
        solicitud={comprobarTarget}
      />
    </div>
  );
}

const TABS_ESTADO = [
  { valor: EstadoSolicitudViaticos.Solicitada, label: 'Solicitada' },
  { valor: EstadoSolicitudViaticos.AutorizadaPorJefe, label: 'Autorizada por jefe' },
  { valor: EstadoSolicitudViaticos.RequiereDireccionFinanzas, label: 'Requiere DF' },
  { valor: EstadoSolicitudViaticos.AutorizadaCompleta, label: 'Autorizada (DF)' },
  { valor: EstadoSolicitudViaticos.Anticipada, label: 'Anticipada' },
  { valor: EstadoSolicitudViaticos.ComprobacionCapturada, label: 'Comprobación capturada' },
  { valor: EstadoSolicitudViaticos.Liquidada, label: 'Liquidada' },
  { valor: EstadoSolicitudViaticos.Rechazada, label: 'Rechazada' },
] as const;

interface FiltrosProps {
  search: ViaticosSearch;
  total: number | undefined;
  onChange: (parcial: Partial<ViaticosSearch>) => void;
}

function Filtros({ search, total, onChange }: FiltrosProps) {
  const algunFiltro = search.empleadoId || search.jefeDirectoId || search.q;
  return (
    <BarraFiltros
      aria-label="Filtros de viáticos"
      busqueda={
        <BusquedaFolio
          aria-label="Buscar por destino"
          valor={search.q}
          onCommit={(q) => onChange({ q })}
          placeholder="Destino"
        />
      }
      filtros={
        <UsuarioSelector
          value={search.empleadoId ?? null}
          onChange={(id) => onChange({ empleadoId: id ?? undefined })}
          placeholder="Todos los empleados"
          className="h-ctl-md w-56 text-xs"
        />
      }
      masFiltros={
        <div className="space-y-1">
          <label className="text-xs text-ink-muted">Jefe directo</label>
          <UsuarioSelector
            value={search.jefeDirectoId ?? null}
            onChange={(id) => onChange({ jefeDirectoId: id ?? undefined })}
            placeholder="Todos"
            className="w-full"
          />
        </div>
      }
      masFiltrosActivos={search.jefeDirectoId ? 1 : 0}
      onLimpiar={
        algunFiltro
          ? () => onChange({ empleadoId: undefined, jefeDirectoId: undefined, q: undefined })
          : undefined
      }
      total={total != null ? `${total} ${total === 1 ? 'solicitud' : 'solicitudes'}` : undefined}
    />
  );
}

interface TablaProps {
  items: readonly SolicitudViaticosListItem[];
  onComprobar: (s: SolicitudViaticosListItem) => void;
}

function Tabla({ items, onComprobar }: TablaProps) {
  const search = useSearch({ from: FROM });
  return (
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Empleado</TableHead>
            <TableHead>Destino / Fechas</TableHead>
            <TableHead className="text-right">Solicitado</TableHead>
            <TableHead className="text-right">Tope</TableHead>
            <TableHead className="text-right">Comprobado</TableHead>
            <TableHead className="text-right">Diferencia</TableHead>
            <TableHead>Estado</TableHead>
            <TableHead className="text-right">
              <span className="sr-only">Acciones</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((s) => {
            const moneda = s.moneda ?? 'MXN';
            return (
              <TableRow key={s.id} className="hover:bg-surface-subtle">
                <TableCell>
                  <div
                    className="max-w-56 truncate"
                    title={s.empleadoNombre ?? undefined}
                  >
                    {s.empleadoNombre?.trim() || <span className="text-ink-muted">Sin asignar</span>}
                  </div>
                  {s.puesto ? <div className="text-2xs text-ink-muted">{s.puesto}</div> : null}
                </TableCell>
                <TableCell>
                  <Link
                    to="/cxp/viaticos/$id"
                    params={{ id: s.id }}
                    search={search}
                    className="font-medium text-brand hover:underline"
                    aria-label={`Abrir viáticos a ${s.destino}`}
                  >
                    {s.destino}
                  </Link>
                  <div className="text-xs text-muted-foreground">
                    <DateTimeDisplay value={s.fechaSalida} variant="corta" /> →{' '}
                    <DateTimeDisplay value={s.fechaRegreso} variant="corta" />
                  </div>
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  <MoneyDisplay amount={s.montoSolicitado} currency={moneda} strong />
                  {s.excedePolitica && (
                    <AlertTriangle
                      className="ml-1 inline h-3 w-3 text-warning-note-fg"
                      aria-label="Excede política"
                    />
                  )}
                </TableCell>
                <TableCell className="text-right tabular-nums text-xs text-ink-muted">
                  <MoneyDisplay amount={s.topePolitica} currency={moneda} />
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  <MoneyDisplay amount={s.montoComprobado} currency={moneda} strong />
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  <MoneyDisplay amount={s.diferenciaLiquidacion} currency={moneda} strong />
                </TableCell>
                <TableCell>
                  <EstadoSolicitudViaticosChip estado={s.estado} />
                </TableCell>
                <TableCell>
                  <div className="flex flex-wrap items-center justify-end gap-1">
                    <AccionesViatico s={s} onComprobar={() => onComprobar(s)} />
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
