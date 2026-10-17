import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { AlertTriangle, Eye, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  UsuarioSelector,
} from '@/components/erp';
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
const SENTINEL_ALL = '__all__';

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
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<ViaticosSearch>) {
    navigate({ to: '/cxp/viaticos', search: { ...search, ...parcial } });
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
                <Plus className="mr-2 h-4 w-4" />
                Solicitar viáticos
              </Button>
            )}
          </>
        }
      />

      <Filtros search={search} onChange={actualizarSearch} />

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
          description="No hay solicitudes con los filtros aplicados."
        />
      ) : (
        <Tabla items={items} onComprobar={setComprobarTarget} />
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

interface FiltrosProps {
  search: ViaticosSearch;
  onChange: (parcial: Partial<ViaticosSearch>) => void;
}

function Filtros({ search, onChange }: FiltrosProps) {
  const algunFiltro =
    search.estado != null || search.empleadoId || search.jefeDirectoId;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as EstadoSolicitudViaticos),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-52">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoSolicitudViaticos.Solicitada)}>
              Solicitada
            </SelectItem>
            <SelectItem
              value={String(EstadoSolicitudViaticos.AutorizadaPorJefe)}
            >
              Autorizada por jefe
            </SelectItem>
            <SelectItem
              value={String(
                EstadoSolicitudViaticos.RequiereDireccionFinanzas,
              )}
            >
              Requiere DF
            </SelectItem>
            <SelectItem
              value={String(EstadoSolicitudViaticos.AutorizadaCompleta)}
            >
              Autorizada (DF)
            </SelectItem>
            <SelectItem value={String(EstadoSolicitudViaticos.Anticipada)}>
              Anticipada
            </SelectItem>
            <SelectItem
              value={String(EstadoSolicitudViaticos.ComprobacionCapturada)}
            >
              Comprobación capturada
            </SelectItem>
            <SelectItem value={String(EstadoSolicitudViaticos.Liquidada)}>
              Liquidada
            </SelectItem>
            <SelectItem value={String(EstadoSolicitudViaticos.Rechazada)}>
              Rechazada
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Empleado</label>
        <UsuarioSelector
          value={search.empleadoId ?? null}
          onChange={(id) => onChange({ empleadoId: id ?? undefined })}
          placeholder="Todos los empleados"
          className="w-56"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Jefe directo</label>
        <UsuarioSelector
          value={search.jefeDirectoId ?? null}
          onChange={(id) => onChange({ jefeDirectoId: id ?? undefined })}
          placeholder="Todos"
          className="w-56"
        />
      </div>
      {algunFiltro && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              estado: undefined,
              empleadoId: undefined,
              jefeDirectoId: undefined,
            })
          }
        >
          Limpiar
        </Button>
      )}
    </div>
  );
}

interface TablaProps {
  items: readonly SolicitudViaticosListItem[];
  onComprobar: (s: SolicitudViaticosListItem) => void;
}

function Tabla({ items, onComprobar }: TablaProps) {
  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Destino / Fechas</th>
            <th className="px-3 py-2 text-right">Solicitado</th>
            <th className="px-3 py-2 text-right">Tope</th>
            <th className="px-3 py-2 text-right">Comprobado</th>
            <th className="px-3 py-2 text-right">Diferencia</th>
            <th className="px-3 py-2 text-left">Estado</th>
            <th className="px-3 py-2 text-right" aria-label="Acciones" />
          </tr>
        </thead>
        <tbody>
          {items.map((s) => (
            <tr key={s.id} className="border-t hover:bg-muted/30">
              <td className="px-3 py-2">
                <div className="font-medium">{s.destino}</div>
                <div className="text-xs text-muted-foreground">
                  <DateTimeDisplay value={s.fechaSalida} variant="corta" /> → <DateTimeDisplay value={s.fechaRegreso} variant="corta" />
                </div>
              </td>
              <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                <MoneyDisplay amount={s.montoSolicitado} currency="MXN" strong />
                {s.excedePolitica && (
                  <AlertTriangle
                    className="ml-1 inline h-3 w-3 text-warning-note-fg"
                    aria-label="Excede política"
                  />
                )}
              </td>
              <td className="py-2 pl-3 pr-4 text-right tabular-nums text-xs text-ink-muted">
                <MoneyDisplay amount={s.topePolitica} currency="MXN" />
              </td>
              <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                <MoneyDisplay amount={s.montoComprobado} currency="MXN" strong />
              </td>
              <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                <MoneyDisplay amount={s.diferenciaLiquidacion} currency="MXN" strong />
              </td>
              <td className="px-3 py-2">
                <EstadoSolicitudViaticosChip estado={s.estado} />
              </td>
              <td className="px-3 py-2 text-right">
                <div className="flex flex-wrap items-center justify-end gap-1">
                  <Button asChild variant="ghost" size="sm">
                    <Link
                      to="/cxp/viaticos/$id"
                      params={{ id: s.id }}
                      aria-label={`Ver detalle de viáticos a ${s.destino}`}
                    >
                      <Eye className="mr-1 h-3 w-3" aria-hidden="true" />
                      Ver detalle
                    </Link>
                  </Button>
                  <AccionesViatico s={s} onComprobar={() => onComprobar(s)} />
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
