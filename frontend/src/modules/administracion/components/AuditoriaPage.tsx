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
import { useDepartamentos, usePuestos, useEmpleados } from '@/features/catalogos/api/hooks';
import { useRoles } from '@/modules/identidad/api/roles';
import { useUsuarios } from '@/modules/identidad/api/usuarios';
import type {
  AuditLogEntryResponse,
  ConsultarBitacoraFiltros,
} from '@/modules/administracion/api';
import { AuditoriaDetalleDrawer } from '@/modules/administracion/components/AuditoriaDetalleDrawer';
import {
  type AuditoriaLookups,
  formatFecha,
  formatHora,
  humanizarTextoConLookups,
  isUuid,
} from './auditoria-utils';

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
  { label: 'Autorización', value: 'autorizacion' },
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
  const empresas = useMemo(() => empresasQuery.data?.items ?? [], [empresasQuery.data?.items]);
  const usuariosQuery = useUsuarios({ limit: 200 });
  const usuarios = useMemo(() => usuariosQuery.data?.items ?? [], [usuariosQuery.data?.items]);

  const departamentosQuery = useDepartamentos();
  const departamentos = useMemo(() => departamentosQuery.data?.items ?? [], [departamentosQuery.data?.items]);
  const puestosQuery = usePuestos();
  const puestos = useMemo(() => puestosQuery.data?.items ?? [], [puestosQuery.data?.items]);
  const empleadosQuery = useEmpleados();
  const empleados = useMemo(() => empleadosQuery.data?.items ?? [], [empleadosQuery.data?.items]);
  const rolesQuery = useRoles({ limit: 200 });
  const roles = useMemo(() => rolesQuery.data?.items ?? [], [rolesQuery.data?.items]);

  const auditoriaQuery = useAuditoria(filtrosAplicados);

  const items = useMemo(() => auditoriaQuery.data?.items ?? [], [auditoriaQuery.data?.items]);
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

  const sucursalesMap = useMemo(() => {
    const map: Record<string, string> = {};
    for (const s of sucursalesQuery.data ?? []) {
      map[s.id] = s.nombre ? `${s.nombre}${s.clave ? ` (${s.clave})` : ''}` : s.clave;
    }
    return map;
  }, [sucursalesQuery.data]);

  const usuariosMap = useMemo(() => {
    const map: Record<string, string> = {};
    for (const u of usuarios) {
      if (u.id) map[u.id] = u.nombre || u.email || "";
    }
    return map;
  }, [usuarios]);

  const usuariosEmailMap = useMemo(() => {
    const map: Record<string, string> = {};
    for (const u of usuarios) {
      if (u.id && u.email) map[u.id] = u.email;
    }
    return map;
  }, [usuarios]);

  const empresasMap = useMemo(() => {
    const map: Record<string, string> = {};
    for (const e of empresas) {
      if (e.id) map[e.id] = e.razonSocial || e.nombreComercial || "";
    }
    return map;
  }, [empresas]);

  const departamentosMap = useMemo(() => {
    const map: Record<string, string> = {};
    for (const d of departamentos) {
      if (d.id) map[d.id] = d.nombre ? `${d.nombre}${d.clave ? ` (${d.clave})` : ''}` : d.clave;
    }
    return map;
  }, [departamentos]);

  const puestosMap = useMemo(() => {
    const map: Record<string, string> = {};
    for (const p of puestos) {
      if (p.id) map[p.id] = p.nombre ? `${p.nombre}${p.clave ? ` (${p.clave})` : ''}` : p.clave;
    }
    return map;
  }, [puestos]);

  const empleadosMap = useMemo(() => {
    const map: Record<string, string> = {};
    for (const emp of empleados) {
      if (emp.id) map[emp.id] = emp.nombre ? `${emp.nombre}${emp.clave ? ` (${emp.clave})` : ''}` : emp.clave;
    }
    return map;
  }, [empleados]);

  const rolesMap = useMemo(() => {
    const map: Record<string, string> = {};
    for (const r of roles) {
      if (r.id) map[r.id] = r.nombre;
    }
    return map;
  }, [roles]);

  const lookups = useMemo<AuditoriaLookups>(() => {
    const sucursales: Record<string, string> = { ...sucursalesMap };
    const usuariosRef: Record<string, string> = { ...usuariosMap };
    const usuariosEmailRef: Record<string, string> = { ...usuariosEmailMap };
    const empresasRef: Record<string, string> = { ...empresasMap };
    const departamentosRef: Record<string, string> = { ...departamentosMap };
    const puestosRef: Record<string, string> = { ...puestosMap };
    const empleadosRef: Record<string, string> = { ...empleadosMap };
    const rolesRef: Record<string, string> = { ...rolesMap };

    for (const item of items) {
      if (item.usuarioId && (item.actorNombre || item.usuarioNombre)) {
        const nom = item.actorNombre || item.usuarioNombre;
        if (nom && !nom.startsWith('Usuario ') && !isUuid(nom)) {
          usuariosRef[item.usuarioId] = nom;
        }
      }
      if (item.usuarioId && item.actorEmail && !usuariosEmailRef[item.usuarioId]) {
        usuariosEmailRef[item.usuarioId] = item.actorEmail;
      }
      if (item.sucursalId && item.sucursalClave && !sucursales[item.sucursalId]) {
        sucursales[item.sucursalId] = item.sucursalClave;
      }
      if (item.entidadId && item.entidadEtiqueta && !isUuid(item.entidadEtiqueta)) {
        if (item.entidad === 'Sucursal' && !sucursales[item.entidadId]) {
          sucursales[item.entidadId] = item.entidadEtiqueta;
        } else if (item.entidad === 'Usuario' && !usuariosRef[item.entidadId]) {
          usuariosRef[item.entidadId] = item.entidadEtiqueta;
        } else if (item.entidad === 'Empresa' && !empresasRef[item.entidadId]) {
          empresasRef[item.entidadId] = item.entidadEtiqueta;
        } else if (item.entidad === 'Departamento' && !departamentosRef[item.entidadId]) {
          departamentosRef[item.entidadId] = item.entidadEtiqueta;
        } else if (item.entidad === 'Puesto' && !puestosRef[item.entidadId]) {
          puestosRef[item.entidadId] = item.entidadEtiqueta;
        } else if (item.entidad === 'Empleado' && !empleadosRef[item.entidadId]) {
          empleadosRef[item.entidadId] = item.entidadEtiqueta;
        } else if (item.entidad === 'Rol' && !rolesRef[item.entidadId]) {
          rolesRef[item.entidadId] = item.entidadEtiqueta;
        }
      }
    }

    return {
      sucursales,
      usuarios: usuariosRef,
      usuariosEmail: usuariosEmailRef,
      empresas: empresasRef,
      departamentos: departamentosRef,
      puestos: puestosRef,
      empleados: empleadosRef,
      roles: rolesRef,
    };
  }, [sucursalesMap, usuariosMap, usuariosEmailMap, empresasMap, departamentosMap, puestosMap, empleadosMap, rolesMap, items]);

  function aplicar() {
    if (rangoInvalido) return;
    setFiltrosAplicados({
      desde: draftDesde,
      hasta: draftHasta,
      q: draftQ.trim() || undefined,
      actorTipo: draftActorTipo || undefined,
      modulo: draftModulo || undefined,
      recurso: draftRecurso || undefined,
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
    const defaultDesde = formatYmd(inicioDefault);
    const defaultHasta = formatYmd(hoy);
    setDraftDesde(defaultDesde);
    setDraftHasta(defaultHasta);
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
      desde: defaultDesde,
      hasta: defaultHasta,
      sucursalId: sucursalActivaId ?? undefined,
      offset: 0,
      limit: PAGE_LIMIT,
    });
  }

  function handleFiltrarPorRegistro(rootId: string) {
    setFiltroAggregateRootId(rootId);
    setFiltrosAplicados((prev) => ({
      ...prev,
      aggregateRootId: rootId,
      offset: 0,
    }));
    setSeleccionado(null);
  }

  function handleQuitarFiltroRegistro() {
    setFiltroAggregateRootId(null);
    setFiltrosAplicados((prev) => {
      const copy = { ...prev };
      delete copy.aggregateRootId;
      return { ...copy, offset: 0 };
    });
  }

  function cambiarPagina(nuevoOffset: number) {
    setFiltrosAplicados((prev) => ({ ...prev, offset: nuevoOffset }));
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">
            Bitácora de auditoría
          </h1>
          <p className="text-sm text-muted-foreground">
            Consulta consolidada de eventos del sistema (rango obligatorio, máx.
            90 días).
          </p>
        </div>
      </div>

      {filtroAggregateRootId && (
        <div className="flex items-center justify-between rounded-md border border-primary/20 bg-primary/5 px-4 py-2 text-sm text-primary">
          <div className="flex items-center gap-2">
            <History className="h-4 w-4" />
            <span>
              Mostrando solo el historial del registro seleccionado (
              <code className="text-xs font-mono">{filtroAggregateRootId.slice(0, 8)}…</code>).
            </span>
          </div>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            className="h-7 text-xs gap-1"
            onClick={handleQuitarFiltroRegistro}
          >
            <X className="h-3.5 w-3.5" />
            Quitar filtro de registro
          </Button>
        </div>
      )}

      {/* Panel de Filtros */}
      <div className="grid gap-3 rounded-lg border bg-card p-4 md:grid-cols-3 lg:grid-cols-4">
        {/* Rango obligatorio */}
        <div className="flex flex-col gap-1">
          <label htmlFor="auditoria-desde" className="text-xs font-medium text-muted-foreground">
            Desde <span className="text-destructive">*</span>
          </label>
          <Input
            id="auditoria-desde"
            type="date"
            className="h-9"
            value={draftDesde}
            onChange={(e) => setDraftDesde(e.target.value)}
          />
        </div>

        <div className="flex flex-col gap-1">
          <label htmlFor="auditoria-hasta" className="text-xs font-medium text-muted-foreground">
            Hasta <span className="text-destructive">*</span>
          </label>
          <Input
            id="auditoria-hasta"
            type="date"
            className="h-9"
            value={draftHasta}
            onChange={(e) => setDraftHasta(e.target.value)}
          />
          {diasRango != null && diasRango > RANGO_MAX_DIAS && (
            <span className="text-[11px] text-destructive">Máximo 90 días.</span>
          )}
        </div>

        {/* Búsqueda de texto */}
        <div className="flex flex-col gap-1">
          <label htmlFor="auditoria-q" className="text-xs font-medium text-muted-foreground">
            Buscar (texto libre)
          </label>
          <Input
            id="auditoria-q"
            type="search"
            placeholder="Actor, resumen, registro..."
            className="h-9"
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
              <SelectItem value={TODOS}>Todos los tipos</SelectItem>
              {ACTOR_TIPOS.map((t) => (
                <SelectItem key={t.value} value={t.value}>
                  {t.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
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

        {/* Recurso / Entidad */}
        <div className="flex flex-col gap-1">
          <label htmlFor="auditoria-recurso" className="text-xs font-medium text-muted-foreground">
            Entidad
          </label>
          <Input
            id="auditoria-recurso"
            placeholder="ej. Requisicion, Empleado..."
            className="h-9"
            value={draftRecurso}
            onChange={(e) => setDraftRecurso(e.target.value)}
          />
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
                  {u.nombre || u.email}
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
            {draftSucursalId && !(sucursalesQuery.data ?? []).some((s) => s.id === draftSucursalId) && (
              <option value={draftSucursalId} hidden>
                {draftSucursalId}
              </option>
            )}
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
        lookups={lookups}
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
              variant="outline"
              size="sm"
              disabled={offset === 0}
              onClick={() => cambiarPagina(Math.max(0, offset - PAGE_LIMIT))}
            >
              Anterior
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={offset + PAGE_LIMIT >= total}
              onClick={() => cambiarPagina(offset + PAGE_LIMIT)}
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
        lookups={lookups}
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
  lookups: AuditoriaLookups;
}

function AuditoriaTabla({
  items,
  isLoading,
  isError,
  error,
  onRetry,
  onVer,
  lookups,
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
    return (
      <ErrorState
        problem={problem}
        title="Error al cargar la bitácora"
        onRetry={onRetry}
      />
    );
  }

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<ClipboardList className="h-10 w-10" />}
        title="Sin registros de auditoría"
        description="No se encontraron eventos en el rango y filtros seleccionados."
      />
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border bg-card">
      <table className="w-full text-sm">
        <thead className="border-b bg-muted/30 text-xs uppercase text-muted-foreground">
          <tr>
            <th scope="col" className="px-3 py-2 text-left">
              Fecha y hora
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
            const actorNombre =
              (entry.actorNombre && !entry.actorNombre.startsWith('Usuario ') && !isUuid(entry.actorNombre) ? entry.actorNombre : null) ??
              (entry.usuarioNombre && !isUuid(entry.usuarioNombre) ? entry.usuarioNombre : null) ??
              (entry.usuarioId ? lookups.usuarios?.[entry.usuarioId] : null) ??
              entry.actorNombre ??
              entry.usuarioNombre ??
              'Sistema';

            const sucursalNombre = entry.sucursalId ? lookups.sucursales?.[entry.sucursalId] : null;

            const etiquetaRegistro =
              (entry.entidadEtiqueta && !isUuid(entry.entidadEtiqueta) && !entry.entidadEtiqueta.startsWith(entry.entidad + ' ')
                ? humanizarTextoConLookups(entry.entidadEtiqueta, lookups)
                : null) ??
              (entry.entidadId && entry.entidad === 'Sucursal' && lookups.sucursales?.[entry.entidadId] ? lookups.sucursales[entry.entidadId] : null) ??
              (entry.entidadId && entry.entidad === 'Usuario' && lookups.usuarios?.[entry.entidadId] ? lookups.usuarios[entry.entidadId] : null) ??
              (entry.entidadId && entry.entidad === 'Empresa' && lookups.empresas?.[entry.entidadId] ? lookups.empresas[entry.entidadId] : null) ??
              (entry.entidadId && entry.entidad === 'Departamento' && lookups.departamentos?.[entry.entidadId] ? lookups.departamentos[entry.entidadId] : null) ??
              (entry.entidadId && entry.entidad === 'Puesto' && lookups.puestos?.[entry.entidadId] ? lookups.puestos[entry.entidadId] : null) ??
              (entry.entidadId && entry.entidad === 'Empleado' && lookups.empleados?.[entry.entidadId] ? lookups.empleados[entry.entidadId] : null) ??
              (entry.entidadId && entry.entidad === 'Rol' && lookups.roles?.[entry.entidadId] ? lookups.roles[entry.entidadId] : null) ??
              (entry.entidadEtiqueta && !isUuid(entry.entidadEtiqueta) ? humanizarTextoConLookups(entry.entidadEtiqueta, lookups) : null) ??
              entry.entidad;

            const resumenHumanizado = humanizarTextoConLookups(entry.resumen || entry.operacion, lookups);

            return (
              <tr key={entry.id} className="hover:bg-muted/10">
                <td className="px-3 py-2 whitespace-nowrap tabular-nums text-xs" title={entry.timestamp}>
                  <div className="font-medium text-foreground">{formatFecha(entry.timestamp)}</div>
                  <div className="text-[11px] text-muted-foreground">{formatHora(entry.timestamp)} hrs</div>
                </td>
                <td className="px-3 py-2">
                  <div className="flex flex-col gap-0.5">
                    <div className="flex items-center gap-1.5 flex-wrap">
                      <span className="font-medium text-foreground">
                        {actorNombre}
                      </span>
                      <Badge
                        variant={actorTipo === 'usuario' ? 'secondary' : 'outline'}
                        className="text-[10px] px-1.5 py-0 capitalize"
                      >
                        {entry.actorTipo || 'usuario'}
                      </Badge>
                    </div>
                    {(() => {
                      const email = entry.actorEmail || (entry.usuarioId ? lookups.usuariosEmail?.[entry.usuarioId] : null);
                      if (email) {
                        return (
                          <span className="text-xs text-muted-foreground">
                            {email}
                          </span>
                        );
                      }
                      if (entry.origen) {
                        return (
                          <span className="text-xs text-muted-foreground">
                            {entry.origen}
                          </span>
                        );
                      }
                      return null;
                    })()}
                  </div>
                </td>
                <td className="px-3 py-2">
                  <span className="text-sm font-medium text-foreground">
                    {resumenHumanizado}
                  </span>
                </td>
                <td className="px-3 py-2">
                  <div className="flex flex-col">
                    <span className="text-sm font-medium text-foreground">
                      {etiquetaRegistro}
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
                  {sucursalNombre ? (
                    <div className="flex flex-col">
                      <span className="text-sm font-medium text-foreground">{sucursalNombre}</span>
                      {entry.sucursalClave && (
                        <span className="text-[11px] text-muted-foreground font-mono">{entry.sucursalClave}</span>
                      )}
                    </div>
                  ) : entry.sucursalClave ? (
                    <span className="text-sm text-muted-foreground font-mono">{entry.sucursalClave}</span>
                  ) : (
                    <span className="text-sm text-muted-foreground">No aplica</span>
                  )}
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
