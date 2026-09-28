import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ClipboardList, Eye, History, X } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
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
} from '@/components/erp';
import { esApiError, apiRequest } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useAuditoria } from '@/modules/administracion/api/auditoria';
import { useEmpresas } from '@/modules/administracion/api';
import { useUsuarios } from '@/modules/identidad/api/usuarios';
import type {
  AuditLogEntryResponse,
  ConsultarBitacoraFiltros,
} from '@/modules/administracion/api';
import { AuditoriaDetalleDrawer } from '@/modules/administracion/components/AuditoriaDetalleDrawer';

const PAGE_LIMIT = 50;
const TODOS = '__todos__';
const RANGO_DEFAULT_DIAS = 7;
const RANGO_MAX_DIAS = 90;

const MODULOS: readonly string[] = [
  'Compras',
  'Identidad',
  'Administracion',
  'Catalogos',
  'DatosMaestros',
  'Compartido',
];

const ACCIONES: readonly { label: string; value: string }[] = [
  { label: 'Crear', value: 'Crear' },
  { label: 'Actualizar', value: 'Actualizar' },
  { label: 'Desactivar', value: 'Desactivar' },
  { label: 'Reactivar', value: 'Reactivar' },
  { label: 'Eliminar', value: 'Eliminar' },
  { label: 'Acceso', value: 'acceso' },
  { label: 'Acceso denegado', value: 'acceso-denegado' },
];

const ACTOR_TIPOS: readonly { label: string; value: string }[] = [
  { label: 'Usuario', value: 'usuario' },
  { label: 'Proceso', value: 'proceso' },
  { label: 'Sistema', value: 'sistema' },
];

/**
 * <c>&lt;AuditoriaPage/&gt;</c> — bandeja P2 (full-width tabular,
 * filtros server-side) del log consolidado <c>core.audit_log</c>
 * (UF-Admin-PR7 §1, ADR-0008, F1-ADM-03).
 *
 * <para>El rango de fechas es obligatorio (default últimos 7 días) y
 * con máximo 90 días. Se muestran datos humanizados (quién, qué hizo,
 * registro, módulo, sucursal) sin GUIDs crudos expuestos al usuario.</para>
 */
export function AuditoriaPage() {
  const hoy = useMemo(() => new Date(), []);
  const inicioDefault = useMemo(() => {
    const d = new Date(hoy);
    d.setDate(d.getDate() - RANGO_DEFAULT_DIAS);
    return d;
  }, [hoy]);

  const empresaActualId = useAuthStore((s) => s.currentEmpresaId);
  const sucursalActivaId = useAuthStore((s) => s.currentSucursalId);

  const [draftDesde, setDraftDesde] = useState(formatYmd(inicioDefault));
  const [draftHasta, setDraftHasta] = useState(formatYmd(hoy));
  const [draftQ, setDraftQ] = useState('');
  const [draftActorTipo, setDraftActorTipo] = useState('');
  const [draftModulo, setDraftModulo] = useState('');
  const [draftRecurso, setDraftRecurso] = useState('');
  const [draftAccion, setDraftAccion] = useState('');
  const [draftUsuarioId, setDraftUsuarioId] = useState('');
  const [draftEmpresaId, setDraftEmpresaId] = useState('');
  const [draftSucursalId, setDraftSucursalId] = useState(sucursalActivaId ?? '');
  const [filtroAggregateRootId, setFiltroAggregateRootId] = useState<string | null>(null);

  const sucursalesQuery = useQuery({
    queryKey: ['auth', 'sucursales', empresaActualId],
    enabled: empresaActualId != null,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<Array<{ id: string; nombre: string; clave: string }>>(
          '/api/auth/sucursales',
          { signal },
        )
      ).data,
  });

  const [filtrosAplicados, setFiltrosAplicados] =
    useState<ConsultarBitacoraFiltros>({
      desde: formatYmd(inicioDefault),
      hasta: formatYmd(hoy),
      sucursalId: sucursalActivaId ?? undefined,
      offset: 0,
      limit: PAGE_LIMIT,
    });

  const empresasQuery = useEmpresas({ limit: 200 });
  const empresas = empresasQuery.data?.items ?? [];
  const usuariosQuery = useUsuarios({ limit: 200 });
  const usuarios = usuariosQuery.data?.items ?? [];

  const auditoriaQuery = useAuditoria(filtrosAplicados);

  const items = auditoriaQuery.data?.items ?? [];
  const total = auditoriaQuery.data?.total ?? 0;
  const offset = filtrosAplicados.offset ?? 0;

  const diasRango = diferenciaDias(draftDesde, draftHasta);
  const rangoInvalido =
    !draftDesde ||
    !draftHasta ||
    diasRango == null ||
    diasRango < 0 ||
    diasRango > RANGO_MAX_DIAS;

  const [seleccionado, setSeleccionado] =
    useState<AuditLogEntryResponse | null>(null);

  function aplicar() {
    if (rangoInvalido) return;
    setFiltrosAplicados({
      desde: draftDesde,
      hasta: draftHasta,
      q: draftQ.trim() || undefined,
      actorTipo: draftActorTipo || undefined,
      modulo: draftModulo || undefined,
      recurso: draftRecurso.trim() || undefined,
      accion: draftAccion || undefined,
      usuarioId: draftUsuarioId || undefined,
      empresaId: draftEmpresaId || undefined,
      sucursalId: draftSucursalId || undefined,
      aggregateRootId: filtroAggregateRootId || undefined,
      offset: 0,
      limit: PAGE_LIMIT,
    });
  }

  function limpiar() {
    setDraftDesde(formatYmd(inicioDefault));
    setDraftHasta(formatYmd(hoy));
    setDraftQ('');
    setDraftActorTipo('');
    setDraftModulo('');
    setDraftRecurso('');
    setDraftAccion('');
    setDraftUsuarioId('');
    setDraftEmpresaId('');
    setDraftSucursalId(sucursalActivaId ?? '');
    setFiltroAggregateRootId(null);
    setFiltrosAplicados({
      desde: formatYmd(inicioDefault),
      hasta: formatYmd(hoy),
      sucursalId: sucursalActivaId ?? undefined,
      offset: 0,
      limit: PAGE_LIMIT,
    });
  }

  function paginar(nuevoOffset: number) {
    setFiltrosAplicados({ ...filtrosAplicados, offset: nuevoOffset });
  }

  function handleFiltrarPorRegistro(aggregateRootId: string) {
    setFiltroAggregateRootId(aggregateRootId);
    setFiltrosAplicados((prev) => ({
      ...prev,
      aggregateRootId,
      offset: 0,
    }));
    setSeleccionado(null);
  }

  function quitarFiltroRegistro() {
    setFiltroAggregateRootId(null);
    setFiltrosAplicados((prev) => ({
      ...prev,
      aggregateRootId: undefined,
      offset: 0,
    }));
  }

  return (
    <div className="space-y-4 p-4">
      <header>
        <h1 className="text-xl font-semibold tracking-tight">
          Bitácora de auditoría
        </h1>
        <p className="text-xs text-muted-foreground">
          Historial consolidado de cambios y accesos. Rango obligatorio, máximo{' '}
          {RANGO_MAX_DIAS} días.
        </p>
      </header>

      {/* Banner de filtro por historial de registro */}
      {filtroAggregateRootId && (
        <div className="flex items-center justify-between rounded-md border border-primary/30 bg-primary/5 px-3 py-2 text-xs">
          <div className="flex items-center gap-2">
            <History className="h-4 w-4 text-primary" />
            <span>
              Mostrando solo el historial del registro seleccionado.
            </span>
          </div>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            className="h-7 text-xs gap-1"
            onClick={quitarFiltroRegistro}
          >
            <X className="h-3.5 w-3.5" />
            Quitar filtro de registro
          </Button>
        </div>
      )}

      <div className="grid gap-3 rounded-md border bg-card p-3 md:grid-cols-3 lg:grid-cols-4">
        {/* Buscador de texto */}
        <div className="flex flex-col gap-1 md:col-span-2 lg:col-span-2">
          <label
            htmlFor="auditoria-q"
            className="text-xs font-medium text-muted-foreground"
          >
            Buscar
          </label>
          <Input
            id="auditoria-q"
            type="text"
            placeholder="Buscar por actor, etiqueta de registro o resumen…"
            value={draftQ}
            onChange={(e) => setDraftQ(e.target.value)}
          />
        </div>

        {/* Tipo de actor */}
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground">
            Tipo de actor
          </label>
          <Select
            value={draftActorTipo || TODOS}
            onValueChange={(v) => setDraftActorTipo(v === TODOS ? '' : v)}
          >
            <SelectTrigger className="h-9">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todos</SelectItem>
              {ACTOR_TIPOS.map((t) => (
                <SelectItem key={t.value} value={t.value}>
                  {t.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Acción */}
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground">
            Acción
          </label>
          <Select
            value={draftAccion || TODOS}
            onValueChange={(v) => setDraftAccion(v === TODOS ? '' : v)}
          >
            <SelectTrigger className="h-9">
              <SelectValue placeholder="Todas" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todas</SelectItem>
              {ACCIONES.map((a) => (
                <SelectItem key={a.value} value={a.value}>
                  {a.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Fechas */}
        <div className="flex flex-col gap-1">
          <label
            htmlFor="auditoria-desde"
            className="text-xs font-medium text-muted-foreground"
          >
            Desde
          </label>
          <Input
            id="auditoria-desde"
            type="date"
            value={draftDesde}
            onChange={(e) => setDraftDesde(e.target.value)}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label
            htmlFor="auditoria-hasta"
            className="text-xs font-medium text-muted-foreground"
          >
            Hasta
          </label>
          <Input
            id="auditoria-hasta"
            type="date"
            value={draftHasta}
            onChange={(e) => setDraftHasta(e.target.value)}
          />
          {diasRango != null && diasRango > RANGO_MAX_DIAS && (
            <p className="text-xs text-destructive">{`Máximo ${RANGO_MAX_DIAS} días.`}</p>
          )}
          {diasRango != null && diasRango < 0 && (
            <p className="text-xs text-destructive">
              &quot;Desde&quot; debe ser anterior a &quot;hasta&quot;.
            </p>
          )}
        </div>

        {/* Módulo */}
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground">
            Módulo
          </label>
          <Select
            value={draftModulo || TODOS}
            onValueChange={(v) => setDraftModulo(v === TODOS ? '' : v)}
          >
            <SelectTrigger className="h-9">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todos</SelectItem>
              {MODULOS.map((m) => (
                <SelectItem key={m} value={m}>
                  {m}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Recurso */}
        <div className="flex flex-col gap-1">
          <label
            htmlFor="auditoria-recurso"
            className="text-xs font-medium text-muted-foreground"
          >
            Recurso
          </label>
          <Input
            id="auditoria-recurso"
            type="text"
            placeholder="Ej. Empleado, Requisicion…"
            value={draftRecurso}
            onChange={(e) => setDraftRecurso(e.target.value)}
          />
        </div>

        {/* Usuario */}
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground">
            Usuario
          </label>
          <Select
            value={draftUsuarioId || TODOS}
            onValueChange={(v) => setDraftUsuarioId(v === TODOS ? '' : v)}
          >
            <SelectTrigger className="h-9">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todos</SelectItem>
              {usuarios.map((u) => (
                <SelectItem key={u.id} value={u.id}>
                  {u.nombre}{' '}
                  <span className="text-xs text-muted-foreground">
                    ({u.email})
                  </span>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Empresa */}
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground">
            Empresa
          </label>
          <Select
            value={draftEmpresaId || TODOS}
            onValueChange={(v) => setDraftEmpresaId(v === TODOS ? '' : v)}
          >
            <SelectTrigger className="h-9">
              <SelectValue placeholder="Todas" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todas</SelectItem>
              {empresas.map((e) => (
                <SelectItem key={e.id} value={e.id}>
                  <span className="font-mono text-xs">{e.rfc}</span> —{' '}
                  {e.razonSocial}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Sucursal */}
        <div className="flex flex-col gap-1">
          <label
            htmlFor="auditoria-sucursal"
            className="text-xs font-medium text-muted-foreground"
          >
            Sucursal
          </label>
          <select
            id="auditoria-sucursal"
            className="h-9 rounded-md border border-input bg-background px-2 text-sm"
            value={draftSucursalId}
            onChange={(e) => setDraftSucursalId(e.target.value)}
          >
            <option value="">Todas las sucursales de la empresa</option>
            {(sucursalesQuery.data ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.clave} · {s.nombre}
              </option>
            ))}
          </select>
        </div>

        {/* Botones de acción */}
        <div className="flex items-end gap-2 md:col-span-3 lg:col-span-4">
          <Button
            type="button"
            size="sm"
            onClick={aplicar}
            disabled={rangoInvalido}
          >
            Aplicar
          </Button>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={limpiar}
          >
            Limpiar
          </Button>
        </div>
      </div>

      <AuditoriaTabla
        items={items}
        isLoading={auditoriaQuery.isLoading || auditoriaQuery.isFetching}
        isError={auditoriaQuery.isError}
        error={auditoriaQuery.error}
        onRetry={() => auditoriaQuery.refetch()}
        onVer={(entry) => setSeleccionado(entry)}
      />

      {total > PAGE_LIMIT && (
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <span>
            Mostrando {offset + 1}–{Math.min(offset + items.length, total)} de{' '}
            {total}
          </span>
          <div className="flex items-center gap-2">
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={offset === 0}
              onClick={() => paginar(Math.max(0, offset - PAGE_LIMIT))}
            >
              Anterior
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={offset + items.length >= total}
              onClick={() => paginar(offset + PAGE_LIMIT)}
            >
              Siguiente
            </Button>
          </div>
        </div>
      )}

      <AuditoriaDetalleDrawer
        entry={seleccionado}
        onOpenChange={(open) => {
          if (!open) setSeleccionado(null);
        }}
        onFiltrarRegistro={handleFiltrarPorRegistro}
      />
    </div>
  );
}

interface AuditoriaTablaProps {
  items: readonly AuditLogEntryResponse[];
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  onRetry: () => void;
  onVer: (entry: AuditLogEntryResponse) => void;
}

function AuditoriaTabla({
  items,
  isLoading,
  isError,
  error,
  onRetry,
  onVer,
}: AuditoriaTablaProps) {
  if (isLoading) {
    return (
      <TableSkeleton
        rows={5}
        columns={[
          { width: 'w-28' },
          { width: 'w-44' },
          { width: 'w-48' },
          { width: 'w-40' },
          { width: 'w-24' },
          { width: 'w-24' },
          { width: 'w-16' },
        ]}
      />
    );
  }

  if (isError) {
    const problem = esApiError(error) ? error.problem : undefined;
    return <ErrorState problem={problem} onRetry={onRetry} />;
  }

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<ClipboardList className="h-10 w-10" />}
        title="Sin movimientos en el rango seleccionado."
        description="Ajusta el rango o los filtros para ver registros."
      />
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border bg-card">
      <table className="w-full text-sm">
        <thead className="border-b bg-muted/30 text-xs uppercase text-muted-foreground">
          <tr>
            <th scope="col" className="px-3 py-2 text-left">
              Fecha
            </th>
            <th scope="col" className="px-3 py-2 text-left">
              Quién
            </th>
            <th scope="col" className="px-3 py-2 text-left">
              Qué hizo
            </th>
            <th scope="col" className="px-3 py-2 text-left">
              Registro
            </th>
            <th scope="col" className="px-3 py-2 text-left">
              Módulo
            </th>
            <th scope="col" className="px-3 py-2 text-left">
              Sucursal
            </th>
            <th scope="col" className="px-3 py-2 text-right">
              Acciones
            </th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((entry) => {
            const actorTipo = entry.actorTipo?.toLowerCase() ?? 'usuario';
            return (
              <tr key={entry.id} className="hover:bg-muted/10">
                <td className="px-3 py-2 tabular-nums text-xs text-muted-foreground whitespace-nowrap">
                  {formatTimestamp(entry.timestamp)}
                </td>
                <td className="px-3 py-2">
                  <div className="flex flex-col gap-0.5">
                    <div className="flex items-center gap-1.5 flex-wrap">
                      <span className="font-medium text-foreground">
                        {entry.actorNombre || entry.usuarioNombre || 'Sistema'}
                      </span>
                      <Badge
                        variant={actorTipo === 'usuario' ? 'secondary' : 'outline'}
                        className="text-[10px] px-1.5 py-0 capitalize"
                      >
                        {entry.actorTipo || 'usuario'}
                      </Badge>
                    </div>
                    {entry.actorEmail ? (
                      <span className="text-xs text-muted-foreground">
                        {entry.actorEmail}
                      </span>
                    ) : entry.origen ? (
                      <span className="text-xs text-muted-foreground">
                        {entry.origen}
                      </span>
                    ) : null}
                  </div>
                </td>
                <td className="px-3 py-2">
                  <span className="text-sm font-medium text-foreground">
                    {entry.resumen || entry.operacion}
                  </span>
                </td>
                <td className="px-3 py-2">
                  <div className="flex flex-col">
                    <span className="text-sm font-medium text-foreground">
                      {entry.entidadEtiqueta || entry.entidad}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {entry.entidad}
                    </span>
                  </div>
                </td>
                <td className="px-3 py-2 whitespace-nowrap">
                  <span className="text-sm">{entry.modulo}</span>
                </td>
                <td className="px-3 py-2 whitespace-nowrap">
                  <span className="text-sm text-muted-foreground">
                    {entry.sucursalClave || 'No aplica'}
                  </span>
                </td>
                <td className="px-3 py-2 text-right whitespace-nowrap">
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={() => onVer(entry)}
                    aria-label="Ver detalle"
                  >
                    <Eye className="mr-1 h-4 w-4" />
                    Ver
                  </Button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function formatYmd(d: Date): string {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

function diferenciaDias(desde: string, hasta: string): number | null {
  if (!desde || !hasta) return null;
  const d1 = new Date(`${desde}T00:00:00Z`).getTime();
  const d2 = new Date(`${hasta}T00:00:00Z`).getTime();
  if (Number.isNaN(d1) || Number.isNaN(d2)) return null;
  return Math.round((d2 - d1) / (24 * 60 * 60 * 1000));
}

function formatTimestamp(ts: string): string {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return ts;
  return d.toLocaleString('es-MX', {
    dateStyle: 'short',
    timeStyle: 'short',
  });
}
