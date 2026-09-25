import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import {
  AlertCircle,
  Building2,
  CheckCircle2,
  Clock,
  KeyRound,
  Mail,
  Pencil,
  Plus,
  Power,
  PowerOff,
  RefreshCw,
  Search,
  Send,
  ShieldAlert,
  Users,
  X,
} from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Card, CardContent } from '@/components/ui/card';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { EstatusCatalogo, TipoSucursal } from '@/modules/administracion/api/types';
import {
  useAccionAccesoColaborador,
  useDesactivarEmpleado,
  useEmpleadosAdmin,
  useReactivarEmpleado,
} from '@/modules/administracion/api';
import { useDepartamentos, usePuestos, useSucursales } from '@/features/catalogos/api';
import type { EmpleadoListItem } from '@/features/catalogos/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { EmpleadoInlineForm } from '@/modules/administracion/components/EmpleadoInlineForm';
import { EmpleadoAccesoPanel } from '@/modules/administracion/components/EmpleadoAccesoPanel';

export type FiltroAcceso =
  | 'todos'
  | 'con-acceso'
  | 'sin-acceso'
  | 'sin-login'
  | 'acceso-inactivo';

function getInitials(nombre: string): string {
  const parts = nombre.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return 'EM';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[1][0]).toUpperCase();
}

function getAvatarColors(str: string): string {
  const colors = [
    'bg-blue-100 text-blue-800 border-blue-200',
    'bg-indigo-100 text-indigo-800 border-indigo-200',
    'bg-violet-100 text-violet-800 border-violet-200',
    'bg-emerald-100 text-emerald-800 border-emerald-200',
    'bg-teal-100 text-teal-800 border-teal-200',
    'bg-amber-100 text-amber-800 border-amber-200',
  ];
  let sum = 0;
  for (let i = 0; i < str.length; i++) sum += str.charCodeAt(i);
  return colors[sum % colors.length];
}

function formatFecha(iso: string | null | undefined): string | null {
  if (!iso) return null;
  try {
    const d = new Date(iso);
    return d.toLocaleDateString('es-MX', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  } catch {
    return null;
  }
}

/**
 * <c>&lt;EmpleadosPage/&gt;</c> — Vista ejecutiva y directorio de empleados
 * con seguimiento en vivo del acceso al ERP y provisión en Microsoft Entra ID.
 *
 * Incluye:
 * - KPIs métricos interactivos superiores
 * - Filtros rápidos por estado de acceso (Con acceso, Sin acceso, Sin login, Inactivos)
 * - Búsqueda por texto y filtros por sucursal y departamento
 * - Acción directa en fila para "Reenviar acceso" a quienes no han iniciado sesión
 * - Panel expandible de acceso a Microsoft Entra ID
 */
export function EmpleadosPage() {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [accesoId, setAccesoId] = useState<string | null>(null);
  const [reenviandoId, setReenviandoId] = useState<string | null>(null);
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<EmpleadoListItem | null>(null);

  // Filtros
  const [filtroAcceso, setFiltroAcceso] = useState<FiltroAcceso>('todos');
  const [busqueda, setBusqueda] = useState('');
  const [filtroSucursal, setFiltroSucursal] = useState<string>('todas');
  const [filtroDepto, setFiltroDepto] = useState<string>('todos');

  const canGestionar = useHasPermission(
    PermisosCanonicos.AdminEmpleadosGestionar,
  );
  const canCrearUsuarios = useHasPermission(
    PermisosCanonicos.IdentidadUsuariosCrear,
  );

  const query = useEmpleadosAdmin();
  const empleados = query.data ?? [];

  const sucursalesQuery = useSucursales();
  const sucursales = sucursalesQuery.data?.items ?? [];

  const deptosQuery = useDepartamentos();
  const departamentos = deptosQuery.data?.items ?? [];

  const puestosQuery = usePuestos();
  const puestoClavePorId = useMemo(
    () => new Map((puestosQuery.data?.items ?? []).map((p) => [p.id, p.clave])),
    [puestosQuery.data],
  );

  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarEmpleado();
  const reactivar = useReactivarEmpleado();
  const reenviar = useAccionAccesoColaborador('reenviar');
  const cambioPendiente = desactivar.isPending || reactivar.isPending;

  function onErrorEstatus(error: Error) {
    if (esApiError(error)) {
      toast.error(error.problem.title, {
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
    } else {
      toast.error('Error al actualizar el empleado.');
    }
    setConfirmDesactivar(null);
  }

  // Métricas
  const metricas = useMemo(() => {
    let conAcceso = 0;
    let sinAcceso = 0;
    let sinLogin = 0;
    let inactivos = 0;
    let activoConAcceso = 0;

    for (const e of empleados) {
      if (e.usuarioId == null) {
        sinAcceso++;
      } else {
        conAcceso++;
        if (e.usuarioActivo === false) {
          inactivos++;
        } else if (e.primerAccesoEn == null || e.estadoAcceso === 1) {
          sinLogin++;
        } else {
          activoConAcceso++;
        }
      }
    }

    return {
      total: empleados.length,
      conAcceso,
      sinAcceso,
      sinLogin,
      inactivos,
      activoConAcceso,
    };
  }, [empleados]);

  // Lista filtrada
  const empleadosFiltrados = useMemo(() => {
    return empleados.filter((e) => {
      // Filtro de acceso
      if (filtroAcceso === 'con-acceso' && e.usuarioId == null) return false;
      if (filtroAcceso === 'sin-acceso' && e.usuarioId != null) return false;
      if (filtroAcceso === 'acceso-inactivo') {
        if (e.usuarioId == null || e.usuarioActivo !== false) return false;
      }
      if (filtroAcceso === 'sin-login') {
        if (
          e.usuarioId == null ||
          e.usuarioActivo === false ||
          (e.primerAccesoEn != null && e.estadoAcceso !== 1)
        ) {
          return false;
        }
      }

      // Filtro de sucursal
      if (filtroSucursal !== 'todas' && e.sucursalId !== filtroSucursal) {
        return false;
      }

      // Filtro de departamento
      if (filtroDepto !== 'todos' && e.departamentoId !== filtroDepto) {
        return false;
      }

      // Búsqueda por texto
      if (busqueda.trim()) {
        const q = busqueda.toLowerCase().trim();
        const coincideNombre = e.nombre.toLowerCase().includes(q);
        const coincideClave = e.clave.toLowerCase().includes(q);
        const coincideEmail = (e.email ?? '').toLowerCase().includes(q);
        const puesto = e.puestoId ? (puestoClavePorId.get(e.puestoId) ?? '') : '';
        const coincidePuesto = puesto.toLowerCase().includes(q);
        if (!coincideNombre && !coincideClave && !coincideEmail && !coincidePuesto) {
          return false;
        }
      }

      return true;
    });
  }, [empleados, filtroAcceso, filtroSucursal, filtroDepto, busqueda, puestoClavePorId]);

  function handleReenviarAcceso(e: EmpleadoListItem) {
    setReenviandoId(e.id);
    reenviar.mutate(
      { empleadoId: e.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Datos de acceso reenviados a ${e.emailContacto || e.email || e.nombre}`, {
            description: 'Se ha reenviado el correo con los datos de acceso al colaborador.',
          });
          setReenviandoId(null);
        },
        onError: (err) => {
          if (esApiError(err)) {
            toast.error(err.problem.title, {
              description: err.problem.detail,
            });
          } else {
            toast.error('No se pudo reenviar el acceso.');
          }
          setReenviandoId(null);
        },
      },
    );
  }

  return (
    <div className="mx-auto max-w-6xl space-y-5 p-4">
      {/* Header */}
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-xl font-bold tracking-tight text-foreground">
            <Users className="h-6 w-6 text-primary" aria-hidden="true" />
            Seguimiento y Directorio de Empleados
          </h1>
          <p className="text-sm text-muted-foreground">
            Gestión de personal de talleres y plantas, estatus de cuentas ERP y
            aprovisionamiento en Microsoft Entra ID.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            size="sm"
            variant="outline"
            onClick={() => query.refetch()}
            disabled={query.isRefetching}
            title="Actualizar lista"
          >
            <RefreshCw
              className={`h-4 w-4 ${query.isRefetching ? 'animate-spin' : ''}`}
            />
          </Button>
          {canGestionar && !agregando && (
            <Button
              size="sm"
              aria-label="Agregar empleado"
              onClick={() => {
                setAgregando(true);
                setEditandoId(null);
              }}
            >
              <Plus className="mr-1.5 h-4 w-4" />
              Agregar empleado
            </Button>
          )}
        </div>
      </header>

      {/* KPI Cards Interactivas */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {/* Total */}
        <Card
          className={`cursor-pointer transition-all hover:shadow-sm ${
            filtroAcceso === 'todos' ? 'ring-2 ring-primary bg-primary/5' : ''
          }`}
          onClick={() => setFiltroAcceso('todos')}
        >
          <CardContent className="p-3.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-muted-foreground">
                Total Empleados
              </span>
              <Users className="h-4 w-4 text-muted-foreground" />
            </div>
            <div className="mt-1.5 flex items-baseline gap-2">
              <span className="text-2xl font-bold text-foreground">
                {metricas.total}
              </span>
              <span className="text-[11px] text-muted-foreground">registrados</span>
            </div>
          </CardContent>
        </Card>

        {/* Acceso Activo */}
        <Card
          className={`cursor-pointer transition-all hover:shadow-sm ${
            filtroAcceso === 'con-acceso' ? 'ring-2 ring-emerald-500 bg-emerald-50/50' : ''
          }`}
          onClick={() => setFiltroAcceso('con-acceso')}
        >
          <CardContent className="p-3.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-emerald-700">
                Con Acceso ERP
              </span>
              <CheckCircle2 className="h-4 w-4 text-emerald-600" />
            </div>
            <div className="mt-1.5 flex items-baseline gap-2">
              <span className="text-2xl font-bold text-emerald-700">
                {metricas.conAcceso}
              </span>
              <span className="text-[11px] text-emerald-600/80">cuentas</span>
            </div>
          </CardContent>
        </Card>

        {/* Sin Iniciar Sesión (Pendientes) */}
        <Card
          className={`cursor-pointer transition-all hover:shadow-sm ${
            filtroAcceso === 'sin-login' ? 'ring-2 ring-amber-500 bg-amber-50/60' : ''
          }`}
          onClick={() => setFiltroAcceso('sin-login')}
        >
          <CardContent className="p-3.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-amber-800">
                Sin Iniciar Sesión
              </span>
              <Clock className="h-4 w-4 text-amber-600" />
            </div>
            <div className="mt-1.5 flex items-baseline gap-2">
              <span className="text-2xl font-bold text-amber-800">
                {metricas.sinLogin}
              </span>
              <span className="text-[11px] font-medium text-amber-700">
                pendientes
              </span>
            </div>
          </CardContent>
        </Card>

        {/* Sin Acceso */}
        <Card
          className={`cursor-pointer transition-all hover:shadow-sm ${
            filtroAcceso === 'sin-acceso' ? 'ring-2 ring-slate-400 bg-slate-50' : ''
          }`}
          onClick={() => setFiltroAcceso('sin-acceso')}
        >
          <CardContent className="p-3.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-muted-foreground">
                Sin Acceso ERP
              </span>
              <KeyRound className="h-4 w-4 text-muted-foreground" />
            </div>
            <div className="mt-1.5 flex items-baseline gap-2">
              <span className="text-2xl font-bold text-slate-700">
                {metricas.sinAcceso}
              </span>
              <span className="text-[11px] text-muted-foreground">solo ficha</span>
            </div>
          </CardContent>
        </Card>

        {/* Accesos Inactivos / Bloqueados */}
        <Card
          className={`cursor-pointer transition-all hover:shadow-sm ${
            filtroAcceso === 'acceso-inactivo' ? 'ring-2 ring-rose-500 bg-rose-50/50' : ''
          }`}
          onClick={() => setFiltroAcceso('acceso-inactivo')}
        >
          <CardContent className="p-3.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-rose-800">
                Acceso Inactivo
              </span>
              <ShieldAlert className="h-4 w-4 text-rose-600" />
            </div>
            <div className="mt-1.5 flex items-baseline gap-2">
              <span className="text-2xl font-bold text-rose-700">
                {metricas.inactivos}
              </span>
              <span className="text-[11px] text-rose-600/80">bloqueados</span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Inline Form de Alta */}
      {agregando && canGestionar && (
        <EmpleadoInlineForm
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {/* Toolbar de Búsqueda y Filtros */}
      <div className="space-y-3 rounded-lg border bg-card p-3 shadow-xs">
        {/* Pills de Filtrado Rápido */}
        <div className="flex flex-wrap items-center gap-1.5 border-b pb-3">
          <Button
            size="sm"
            variant={filtroAcceso === 'todos' ? 'default' : 'ghost'}
            className="h-8 text-xs font-medium"
            onClick={() => setFiltroAcceso('todos')}
          >
            Todos ({metricas.total})
          </Button>
          <Button
            size="sm"
            variant={filtroAcceso === 'con-acceso' ? 'default' : 'ghost'}
            className={`h-8 text-xs font-medium ${
              filtroAcceso !== 'con-acceso' ? 'text-emerald-700 hover:text-emerald-800' : ''
            }`}
            onClick={() => setFiltroAcceso('con-acceso')}
          >
            Con acceso ERP ({metricas.conAcceso})
          </Button>
          <Button
            size="sm"
            variant={filtroAcceso === 'sin-login' ? 'default' : 'ghost'}
            className={`h-8 text-xs font-medium gap-1.5 ${
              filtroAcceso !== 'sin-login'
                ? 'text-amber-800 hover:text-amber-900 bg-amber-50/50'
                : 'bg-amber-600 hover:bg-amber-700 text-white'
            }`}
            onClick={() => setFiltroAcceso('sin-login')}
          >
            <span className="h-2 w-2 rounded-full bg-amber-400" />
            Sin iniciar sesión ({metricas.sinLogin})
          </Button>
          <Button
            size="sm"
            variant={filtroAcceso === 'sin-acceso' ? 'default' : 'ghost'}
            className="h-8 text-xs font-medium text-muted-foreground"
            onClick={() => setFiltroAcceso('sin-acceso')}
          >
            Sin acceso ({metricas.sinAcceso})
          </Button>
          <Button
            size="sm"
            variant={filtroAcceso === 'acceso-inactivo' ? 'default' : 'ghost'}
            className={`h-8 text-xs font-medium gap-1.5 ${
              filtroAcceso !== 'acceso-inactivo'
                ? 'text-rose-700 hover:text-rose-800 bg-rose-50/40'
                : 'bg-rose-600 hover:bg-rose-700 text-white'
            }`}
            onClick={() => setFiltroAcceso('acceso-inactivo')}
          >
            <span className="h-2 w-2 rounded-full bg-rose-400" />
            Bloqueados ({metricas.inactivos})
          </Button>
        </div>

        {/* Inputs de Búsqueda y Dropdowns */}
        <div className="flex flex-wrap items-center gap-2 pt-0.5">
          <div className="relative min-w-[240px] flex-1">
            <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Buscar por nombre, clave, correo o puesto..."
              value={busqueda}
              onChange={(e) => setBusqueda(e.target.value)}
              className="h-9 pl-9 pr-8 text-sm"
            />
            {busqueda && (
              <button
                type="button"
                onClick={() => setBusqueda('')}
                className="absolute right-2.5 top-2.5 text-muted-foreground hover:text-foreground"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>

          {/* Filtro Sucursal */}
          <select
            value={filtroSucursal}
            onChange={(e) => setFiltroSucursal(e.target.value)}
            className="h-9 rounded-md border border-input bg-background px-2.5 text-xs text-foreground shadow-xs focus:outline-hidden focus:ring-1 focus:ring-ring"
            aria-label="Filtrar por sucursal"
          >
            <option value="todas">Todas las sucursales</option>
            {sucursales.map((s) => (
              <option key={s.id} value={s.id}>
                {s.nombre} ({s.tipo === TipoSucursal.Planta ? 'Planta' : 'Taller'})
              </option>
            ))}
          </select>

          {/* Filtro Departamento */}
          <select
            value={filtroDepto}
            onChange={(e) => setFiltroDepto(e.target.value)}
            className="h-9 rounded-md border border-input bg-background px-2.5 text-xs text-foreground shadow-xs focus:outline-hidden focus:ring-1 focus:ring-ring"
            aria-label="Filtrar por departamento"
          >
            <option value="todos">Todos los departamentos</option>
            {departamentos.map((d) => (
              <option key={d.id} value={d.id}>
                {d.nombre}
              </option>
            ))}
          </select>

          {(busqueda || filtroSucursal !== 'todas' || filtroDepto !== 'todos') && (
            <Button
              size="sm"
              variant="ghost"
              className="h-9 text-xs"
              onClick={() => {
                setBusqueda('');
                setFiltroSucursal('todas');
                setFiltroDepto('todos');
              }}
            >
              Limpiar
            </Button>
          )}
        </div>
      </div>

      {/* Lista / Tabla de Colaboradores */}
      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los empleados"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={6} />
      ) : empleadosFiltrados.length === 0 ? (
        <div className="rounded-lg border border-dashed bg-muted/20 px-4 py-10 text-center">
          <Users className="mx-auto h-8 w-8 text-muted-foreground/60" />
          <p className="mt-2 text-sm font-medium text-foreground">
            No se encontraron empleados con los filtros aplicados
          </p>
          <p className="text-xs text-muted-foreground mt-0.5">
            Intenta cambiar los términos de búsqueda o el filtro de estado de acceso.
          </p>
          {(filtroAcceso !== 'todos' || busqueda || filtroSucursal !== 'todas') && (
            <Button
              size="sm"
              variant="outline"
              className="mt-3 text-xs"
              onClick={() => {
                setFiltroAcceso('todos');
                setBusqueda('');
                setFiltroSucursal('todas');
                setFiltroDepto('todos');
              }}
            >
              Restablecer filtros
            </Button>
          )}
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg border bg-card shadow-xs">
          <div className="border-b bg-muted/30 px-4 py-2.5 text-xs font-semibold text-muted-foreground flex items-center justify-between">
            <span>
              Mostrando {empleadosFiltrados.length} de {empleados.length} colaboradores
            </span>
            <span className="hidden sm:inline text-[11px] text-muted-foreground">
              {metricas.sinLogin > 0
                ? `⚡ ${metricas.sinLogin} colaboradores pendientes de iniciar sesión`
                : '✓ Todos los accesos activos han iniciado sesión'}
            </span>
          </div>

          <ul className="divide-y divide-border">
            {empleadosFiltrados.map((e) => {
              const editando = editandoId === e.id;
              const activo = e.estatus === EstatusCatalogo.Activo;
              const tieneUsuario = e.usuarioId != null;
              const usuarioBloqueado = tieneUsuario && e.usuarioActivo === false;
              const pendientePrimerAcceso =
                tieneUsuario &&
                !usuarioBloqueado &&
                (e.primerAccesoEn == null || e.estadoAcceso === 1);
              const enProvision = e.estadoAcceso === 2;
              const errorProvision = e.estadoAcceso === 3;
              const accesoActivo =
                tieneUsuario && !usuarioBloqueado && !pendientePrimerAcceso && !enProvision && !errorProvision;

              const isReenviando = reenviandoId === e.id;

              return (
                <li key={e.id} className="p-3.5 transition-colors hover:bg-muted/15">
                  {editando && canGestionar ? (
                    <EmpleadoInlineForm
                      empleado={e}
                      onCancel={() => setEditandoId(null)}
                      onSaved={() => setEditandoId(null)}
                    />
                  ) : (
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                      {/* Información Principal del Empleado */}
                      <div className="flex items-start gap-3 min-w-0 sm:flex-1">
                        {/* Avatar con Iniciales */}
                        <div
                          className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full border text-xs font-bold ${getAvatarColors(
                            e.clave + e.nombre,
                          )}`}
                          title={`Clave: ${e.clave}`}
                        >
                          {getInitials(e.nombre)}
                        </div>

                        <div className="min-w-0 flex-1 space-y-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <Link
                              to="/admin/empleados/$id"
                              params={{ id: e.id }}
                              className="font-semibold text-sm text-foreground hover:text-primary hover:underline truncate"
                            >
                              {e.nombre}
                            </Link>
                            <span className="font-mono text-xs text-muted-foreground bg-muted/60 px-1.5 py-0.5 rounded">
                              {e.clave}
                            </span>
                            {activo ? (
                              <span className="inline-flex items-center text-[11px] font-medium text-emerald-700 bg-emerald-50 border border-emerald-200 rounded-full px-2 py-0.5">
                                Activo
                              </span>
                            ) : (
                              <span className="inline-flex items-center text-[11px] font-medium text-muted-foreground bg-muted border rounded-full px-2 py-0.5">
                                Inactivo
                              </span>
                            )}
                          </div>

                          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                            {e.email && (
                              <span className="flex items-center gap-1 truncate text-foreground/80">
                                <Mail className="h-3 w-3 text-muted-foreground" />
                                {e.email}
                              </span>
                            )}
                            <span className="font-medium text-foreground/90">
                              {e.puestoNombre || (e.puestoId ? (puestoClavePorId.get(e.puestoId) ?? '…') : 'Sin puesto')}
                            </span>
                            {e.departamentoNombre && (
                              <span>· {e.departamentoNombre}</span>
                            )}
                            {e.sucursalNombre && (
                              <span className="inline-flex items-center gap-1 font-medium">
                                <Building2 className="h-3 w-3 text-muted-foreground" />
                                {e.sucursalNombre}
                                {e.sucursalTipo === TipoSucursal.Planta ? (
                                  <span className="text-[10px] font-semibold text-purple-700 bg-purple-50 border border-purple-200 px-1.5 py-0.2 rounded">
                                    Planta
                                  </span>
                                ) : (
                                  <span className="text-[10px] font-semibold text-blue-700 bg-blue-50 border border-blue-200 px-1.5 py-0.2 rounded">
                                    Taller
                                  </span>
                                )}
                              </span>
                            )}
                          </div>
                        </div>
                      </div>

                      {/* Estado de Acceso ERP & Acciones Rápidas */}
                      <div className="flex flex-wrap items-center gap-2.5 sm:self-center">
                        {/* Badge de Acceso ERP */}
                        <div className="flex flex-col items-start sm:items-end min-w-[170px]">
                          {accesoActivo && (
                            <Badge className="bg-emerald-100/80 text-emerald-800 border-emerald-200 hover:bg-emerald-100 flex items-center gap-1 text-xs py-0.5 px-2">
                              <CheckCircle2 className="h-3.5 w-3.5 text-emerald-600" />
                              Acceso Activo
                            </Badge>
                          )}

                          {pendientePrimerAcceso && (
                            <Badge className="bg-amber-100/80 text-amber-900 border-amber-300 hover:bg-amber-100 flex items-center gap-1 text-xs py-0.5 px-2">
                              <Clock className="h-3.5 w-3.5 text-amber-600" />
                              Pendiente 1er Acceso
                            </Badge>
                          )}

                          {usuarioBloqueado && (
                            <Badge className="bg-rose-100/80 text-rose-900 border-rose-300 hover:bg-rose-100 flex items-center gap-1 text-xs py-0.5 px-2">
                              <ShieldAlert className="h-3.5 w-3.5 text-rose-600" />
                              Usuario Bloqueado
                            </Badge>
                          )}

                          {enProvision && (
                            <Badge className="bg-sky-100 text-sky-800 border-sky-300 flex items-center gap-1 text-xs py-0.5 px-2">
                              <RefreshCw className="h-3 w-3 animate-spin text-sky-600" />
                              Creando cuenta Entra
                            </Badge>
                          )}

                          {errorProvision && (
                            <Badge className="bg-destructive/15 text-destructive border-destructive/30 flex items-center gap-1 text-xs py-0.5 px-2">
                              <AlertCircle className="h-3.5 w-3.5" />
                              Error de provisión
                            </Badge>
                          )}

                          {!tieneUsuario && (
                            <Badge
                              variant="outline"
                              className="text-muted-foreground border-dashed text-xs py-0.5 px-2"
                            >
                              Sin acceso ERP
                            </Badge>
                          )}

                          {/* Subtexto descriptivo del acceso */}
                          <div className="text-[11px] text-muted-foreground mt-0.5 truncate max-w-[200px]">
                            {accesoActivo && e.primerAccesoEn && (
                              <span>1er login: {formatFecha(e.primerAccesoEn)}</span>
                            )}
                            {pendientePrimerAcceso && (
                              <span title={`Destino: ${e.emailContacto ?? 'correo personal'}`}>
                                {e.accesoEnviadoEn
                                  ? `Enviado ${formatFecha(e.accesoEnviadoEn)}`
                                  : e.emailContacto
                                    ? `Enviar a ${e.emailContacto}`
                                    : 'Credencial pendiente'}
                              </span>
                            )}
                            {enProvision && <span>Creando cuenta en Entra…</span>}
                            {!tieneUsuario && <span>Solo ficha de personal</span>}
                          </div>
                        </div>

                        {/* Botones de Acción Rápida */}
                        <div className="flex items-center gap-1">
                          {/* Botón Destacado: Reenviar Acceso */}
                          {pendientePrimerAcceso && canCrearUsuarios && activo && (
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-8 border-amber-300 bg-amber-50 text-amber-900 hover:bg-amber-100 shadow-2xs font-medium text-xs gap-1"
                              disabled={isReenviando || reenviar.isPending}
                              onClick={() => handleReenviarAcceso(e)}
                              title="Reenviar las credenciales al correo de contacto"
                            >
                              <Send
                                className={`h-3.5 w-3.5 text-amber-700 ${
                                  isReenviando ? 'animate-pulse' : ''
                                }`}
                              />
                              {isReenviando ? 'Reenviando…' : 'Reenviar acceso'}
                            </Button>
                          )}

                          {/* Botón Dar Acceso si no tiene cuenta */}
                          {!tieneUsuario && canGestionar && canCrearUsuarios && activo && (
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-8 text-xs gap-1"
                              onClick={() => setAccesoId(accesoId === e.id ? null : e.id)}
                            >
                              <KeyRound className="h-3.5 w-3.5" />
                              Dar acceso
                            </Button>
                          )}

                          {/* Botón Gestión de Acceso Avanzado */}
                          {tieneUsuario && canGestionar && (
                            <Button
                              variant="ghost"
                              size="sm"
                              className={`h-8 px-2 text-xs ${
                                accesoId === e.id ? 'bg-muted text-foreground' : ''
                              }`}
                              onClick={() => setAccesoId(accesoId === e.id ? null : e.id)}
                              title={`Gestionar cuenta de ${e.nombre}`}
                            >
                              <KeyRound className="h-3.5 w-3.5 mr-1" />
                              Acceso
                            </Button>
                          )}

                          {/* Botón Editar Empleado */}
                          {canGestionar && (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="h-8 w-8 p-0"
                              aria-label={`Editar empleado ${e.clave}`}
                              onClick={() => {
                                setEditandoId(e.id);
                                setAgregando(false);
                              }}
                              title={`Editar ficha de ${e.nombre}`}
                            >
                              <Pencil className="h-3.5 w-3.5" />
                            </Button>
                          )}

                          {/* Botón Desactivar / Reactivar */}
                          {canGestionar && (
                            activo ? (
                              <Button
                                variant="ghost"
                                size="sm"
                                className="h-8 w-8 p-0 text-muted-foreground hover:text-destructive"
                                aria-label={`Desactivar empleado ${e.clave}`}
                                onClick={() => setConfirmDesactivar(e)}
                                title={`Desactivar empleado ${e.clave}`}
                              >
                                <PowerOff className="h-3.5 w-3.5" />
                              </Button>
                            ) : (
                              <Button
                                variant="ghost"
                                size="sm"
                                className="h-8 w-8 p-0 text-emerald-600 hover:text-emerald-700"
                                aria-label={`Reactivar empleado ${e.clave}`}
                                disabled={cambioPendiente}
                                onClick={() =>
                                  reactivar.mutate(
                                    { id: e.id, idempotencyKey },
                                    {
                                      onSuccess: () =>
                                        toast.success(`Empleado "${e.nombre}" reactivado`),
                                      onError: onErrorEstatus,
                                    },
                                  )
                                }
                                title={`Reactivar empleado ${e.clave}`}
                              >
                                <Power className="h-3.5 w-3.5" />
                              </Button>
                            )
                          )}
                        </div>
                      </div>
                    </div>
                  )}

                  {/* Panel Desplegable de Gestión de Acceso */}
                  {accesoId === e.id && (
                    <div className="mt-3 pt-3 border-t border-dashed">
                      <EmpleadoAccesoPanel
                        empleadoId={e.id}
                        usuarioId={e.usuarioId}
                        email={e.email}
                        empleadoActivo={activo}
                      />
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      )}

      {/* Modal de Confirmación para Desactivar Empleado */}
      <AlertDialog
        open={confirmDesactivar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar empleado</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas dar de baja a{' '}
              <span className="font-semibold text-foreground">
                {confirmDesactivar?.nombre}
              </span>
              ? Saldrá de los selectores operativos (viáticos, comprobaciones); su
              historial se conserva. Si tenía acceso al ERP, su usuario quedará
              bloqueado. En una recontratación, el acceso se reactiva por separado.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={cambioPendiente}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivar == null) return;
                desactivar.mutate(
                  { id: confirmDesactivar.id, idempotencyKey },
                  {
                    onSuccess: () => {
                      toast.success(
                        `Empleado "${confirmDesactivar.nombre}" desactivado`,
                      );
                      setConfirmDesactivar(null);
                    },
                    onError: onErrorEstatus,
                  },
                );
              }}
              disabled={cambioPendiente}
            >
              {cambioPendiente ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
