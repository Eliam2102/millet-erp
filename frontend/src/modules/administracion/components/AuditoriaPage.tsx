import { useMemo, useState } from 'react';
import { ClipboardList, Eye } from 'lucide-react';
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
import { esApiError } from '@/lib/api';
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

const ACCIONES: readonly string[] = [
  'Crear',
  'Actualizar',
  'Desactivar',
  'Reactivar',
  'Eliminar',
];

/**
 * <c>&lt;AuditoriaPage/&gt;</c> — bandeja P2 (full-width tabular,
 * filtros server-side) del log consolidado <c>core.audit_log</c>
 * (UF-Admin-PR7 §1, ADR-0008).
 *
 * <para>El rango de fechas es obligatorio (default últimos 7 días) y
 * con máximo 90 días — el backend rechaza con 400 si se excede; el
 * frontend además bloquea el botón "Aplicar" para evitar el roundtrip
 * inútil. Datos sensibles → no se cachean (staleTime 0 en el hook).</para>
 *
 * <para>El nombre del usuario llega <c>null</c> hasta que el backend
 * resuelva el enrich cross-schema
 * (PLATFORM-TODO &lt;AuditUsuarioEnrich&gt;) — la tabla cae al
 * <c>usuarioId</c> truncado con tooltip explicativo.</para>
 */
export function AuditoriaPage() {
  const hoy = useMemo(() => new Date(), []);
  const inicioDefault = useMemo(() => {
    const d = new Date(hoy);
    d.setDate(d.getDate() - RANGO_DEFAULT_DIAS);
    return d;
  }, [hoy]);

  // Filtros "borrador" — solo se aplican al hacer click en "Aplicar".
  // Esto evita que cada keystroke dispare una query al backend con el
  // costo de un table scan parcial (el rango es obligatorio pero el
  // resto de filtros son optimización).
  const [draftDesde, setDraftDesde] = useState(formatYmd(inicioDefault));
  const [draftHasta, setDraftHasta] = useState(formatYmd(hoy));
  const [draftModulo, setDraftModulo] = useState<string>('');
  const [draftRecurso, setDraftRecurso] = useState('');
  const [draftAccion, setDraftAccion] = useState<string>('');
  const [draftUsuarioId, setDraftUsuarioId] = useState<string>('');
  const [draftEmpresaId, setDraftEmpresaId] = useState<string>('');

  const [filtrosAplicados, setFiltrosAplicados] =
    useState<ConsultarBitacoraFiltros>({
      desde: formatYmd(inicioDefault),
      hasta: formatYmd(hoy),
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
      modulo: draftModulo || undefined,
      recurso: draftRecurso.trim() || undefined,
      accion: draftAccion || undefined,
      usuarioId: draftUsuarioId || undefined,
      empresaId: draftEmpresaId || undefined,
      offset: 0,
      limit: PAGE_LIMIT,
    });
  }

  function limpiar() {
    setDraftDesde(formatYmd(inicioDefault));
    setDraftHasta(formatYmd(hoy));
    setDraftModulo('');
    setDraftRecurso('');
    setDraftAccion('');
    setDraftUsuarioId('');
    setDraftEmpresaId('');
    setFiltrosAplicados({
      desde: formatYmd(inicioDefault),
      hasta: formatYmd(hoy),
      offset: 0,
      limit: PAGE_LIMIT,
    });
  }

  function paginar(nuevoOffset: number) {
    setFiltrosAplicados({ ...filtrosAplicados, offset: nuevoOffset });
  }

  return (
    <div className="space-y-4 p-4">
      <header>
        <h1 className="text-xl font-semibold tracking-tight">
          Bitácora de auditoría
        </h1>
        <p className="text-xs text-muted-foreground">
          Historial consolidado de cambios. Rango obligatorio, máximo{' '}
          {RANGO_MAX_DIAS} días.
        </p>
      </header>

      <div className="grid gap-3 rounded-md border bg-card p-3 md:grid-cols-3 lg:grid-cols-4">
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
            placeholder="Ej. Empresa, Serie…"
            value={draftRecurso}
            onChange={(e) => setDraftRecurso(e.target.value)}
          />
        </div>

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
                <SelectItem key={a} value={a}>
                  {a}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

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
          { width: 'w-32' },
          { width: 'w-40' },
          { width: 'w-32' },
          { width: 'w-32' },
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
              Usuario
            </th>
            <th scope="col" className="px-3 py-2 text-left">
              Módulo
            </th>
            <th scope="col" className="px-3 py-2 text-left">
              Entidad
            </th>
            <th scope="col" className="px-3 py-2 text-left">
              Acción
            </th>
            <th scope="col" className="px-3 py-2 text-right">
              Acciones
            </th>
          </tr>
        </thead>
        <tbody className="divide-y">
          {items.map((entry) => (
            <tr key={entry.id}>
              <td className="px-3 py-2 tabular-nums">
                {formatTimestamp(entry.timestamp)}
              </td>
              <td className="px-3 py-2">
                <UsuarioCell
                  nombre={entry.usuarioNombre}
                  usuarioId={entry.usuarioId}
                />
              </td>
              <td className="px-3 py-2">{entry.modulo}</td>
              <td className="px-3 py-2">
                <div>{entry.entidad}</div>
                {entry.entidadId != null && (
                  <div className="font-mono text-[10px] text-muted-foreground">
                    {truncateId(entry.entidadId)}
                  </div>
                )}
              </td>
              <td className="px-3 py-2">
                <Badge variant="outline">{entry.operacion}</Badge>
              </td>
              <td className="px-3 py-2 text-right">
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
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Renderiza el nombre del usuario; si el backend aún no resuelve el
 * enriquecimiento cross-schema, cae al ID truncado con tooltip nativo.
 *
 * PLATFORM-TODO(&lt;AuditUsuarioEnrich&gt;): cuando el backend popule
 * <c>UsuarioNombre</c> via JOIN/projection cross-schema, esta celda
 * dejará de mostrar el ID y el tooltip se vuelve obsoleto. Eliminar
 * el branch del fallback.
 */
function UsuarioCell({
  nombre,
  usuarioId,
}: {
  nombre: string | null;
  usuarioId: string | null;
}) {
  if (nombre != null && nombre.length > 0) {
    return <span>{nombre}</span>;
  }
  if (usuarioId == null) {
    return <span className="text-muted-foreground">—</span>;
  }
  return (
    <span
      className="font-mono text-xs text-muted-foreground"
      title="Nombre no disponible — pendiente de enriquecimiento PLATFORM-TODO(<AuditUsuarioEnrich>)"
    >
      {truncateId(usuarioId)}
    </span>
  );
}

function truncateId(id: string): string {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id;
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
  return d.toLocaleString();
}

