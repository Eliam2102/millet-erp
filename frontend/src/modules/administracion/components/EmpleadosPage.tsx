import { DialogBody } from '@/components/ui/dialog';
import { ConfirmacionContent as AlertDialogContent } from '@/modules/administracion/components/ConfirmacionContent';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, TabsEstado, PaginacionTabla } from '@/components/erp';
import { ESTADOS_ACCESO, ESTADOS_CATALOGO } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import {
  AlertCircle,
  ArrowRightLeft,
  Building2,
  KeyRound,
  Mail,
  Pencil,
  Plus,
  Power,
  PowerOff,
  RefreshCw,
  Search,
  Send,
  Users,
  X,
} from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
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
import { useHasAllPermissions, useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, codigoSoporte } from '@/lib/api';
import { EmpleadoInlineForm } from '@/modules/administracion/components/EmpleadoInlineForm';
import { EmpleadoAccesoPanel } from '@/modules/administracion/components/EmpleadoAccesoPanel';
import { CuentasSinEmpleado } from '@/modules/administracion/components/CuentasSinEmpleado';
import {
  useCuentasSinEmpleado,
  type CuentaSinEmpleado,
} from '@/modules/administracion/api/empleados';

export type FiltroAcceso =
  | 'todos'
  | 'con-acceso'
  | 'sin-acceso'
  | 'sin-login'
  | 'acceso-inactivo'
  | 'por-reasignar';

function getInitials(nombre: string): string {
  const parts = nombre.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return 'EM';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[1][0]).toUpperCase();
}

function getAvatarColors(str: string): string {
  const colors = [
    'bg-info-bg text-info-fg border-line',
    'bg-info-bg text-info-fg border-line',
    'bg-info-bg text-info-fg border-line',
    'bg-success-bg text-success-fg border-line',
    'bg-success-bg text-success-fg border-line',
    'bg-warning-bg text-warning-fg border-line',
  ];
  let sum = 0;
  for (let i = 0; i < str.length; i++) sum += str.charCodeAt(i);
  return colors[sum % colors.length];
}

function getMotivosReasignacion(
  e: EmpleadoListItem,
  sucursalesMap: Map<string, { estatus: EstatusCatalogo }>,
  deptosMap: Map<string, { estatus: EstatusCatalogo }>,
  puestosMap: Map<string, { estatus: EstatusCatalogo }>,
): string[] {
  if (e.estatus !== EstatusCatalogo.Activo) return [];
  const motivos: string[] = [];
  if (e.sucursalId && sucursalesMap.get(e.sucursalId)?.estatus === EstatusCatalogo.Inactivo) {
    motivos.push('Sucursal inactiva');
  }

  if (!e.departamentoId) {
    motivos.push('Sin depto');
  } else if (deptosMap.get(e.departamentoId)?.estatus === EstatusCatalogo.Inactivo) {
    motivos.push('Depto inactivo');
  }

  if (!e.puestoId) {
    motivos.push('Sin puesto');
  } else if (puestosMap.get(e.puestoId)?.estatus === EstatusCatalogo.Inactivo) {
    motivos.push('Puesto inactivo');
  }

  return motivos;
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
export type VistaEmpleados = 'empleados' | 'sin-empleado';

/** Índice de la pestaña «Cuentas sin empleado» en <c>TabsEstado</c>. */
const TAB_SIN_EMPLEADO = 5;

export function EmpleadosPage({ vistaInicial = 'empleados' }: { vistaInicial?: VistaEmpleados }) {
  const [offset, setOffset] = useState(0);
  const filtrosAcceso: Exclude<FiltroAcceso, 'todos'>[] = [
    'con-acceso',
    'sin-login',
    'sin-acceso',
    'acceso-inactivo',
    'por-reasignar',
  ];
  const [agregando, setAgregando] = useState(false);
  const [cuentaParaAlta, setCuentaParaAlta] = useState<CuentaSinEmpleado | null>(null);
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [accesoId, setAccesoId] = useState<string | null>(null);
  const [reenviandoId, setReenviandoId] = useState<string | null>(null);
  const [confirmDesactivar, setConfirmDesactivar] = useState<EmpleadoListItem | null>(null);

  // Filtros
  const [filtroAcceso, setFiltroAcceso] = useState<FiltroAcceso>('todos');
  const [busqueda, setBusqueda] = useState('');
  const [filtroSucursal, setFiltroSucursal] = useState<string>('todas');
  const [filtroDepto, setFiltroDepto] = useState<string>('todos');

  const canGestionar = useHasPermission(PermisosCanonicos.AdminEmpleadosGestionar);
  // Contrato 1 (86e3x6cpw): la vista exige ambos permisos y perfil corporativo,
  // porque la cuenta aún no tiene sucursal.
  const canVerSinEmpleado = useHasAllPermissions([
    PermisosCanonicos.AdminEmpleadosGestionar,
    PermisosCanonicos.IdentidadUsuariosCrear,
    PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales,
  ]);
  const [vistaElegida, setVista] = useState<VistaEmpleados>(vistaInicial);
  const vista: VistaEmpleados = canVerSinEmpleado ? vistaElegida : 'empleados';
  const cuentasSinEmpleado = useCuentasSinEmpleado('', canVerSinEmpleado);
  const canCrearUsuarios = useHasPermission(PermisosCanonicos.IdentidadUsuariosCrear);
  // La API de dar acceso exige ambos permisos.
  const canDarAcceso = useHasAllPermissions([
    PermisosCanonicos.IdentidadUsuariosCrear,
    PermisosCanonicos.IdentidadAsignacionesAdministrar,
  ]);

  const query = useEmpleadosAdmin();
  const empleados = useMemo(() => query.data ?? [], [query.data]);

  const sucursalesQuery = useSucursales();
  const sucursales = sucursalesQuery.data?.items ?? [];

  const deptosQuery = useDepartamentos();
  const departamentos = deptosQuery.data?.items ?? [];

  const puestosQuery = usePuestos();
  const puestoClavePorId = useMemo(
    () => new Map((puestosQuery.data?.items ?? []).map((p) => [p.id, p.clave])),
    [puestosQuery.data],
  );

  const sucursalesMap = useMemo(
    () => new Map((sucursalesQuery.data?.items ?? []).map((s) => [s.id, s])),
    [sucursalesQuery.data],
  );

  const deptosMap = useMemo(
    () => new Map((deptosQuery.data?.items ?? []).map((d) => [d.id, d])),
    [deptosQuery.data],
  );

  const puestosMap = useMemo(
    () => new Map((puestosQuery.data?.items ?? []).map((p) => [p.id, p])),
    [puestosQuery.data],
  );

  const desactivar = useDesactivarEmpleado();
  const reactivar = useReactivarEmpleado();
  const reenviar = useAccionAccesoColaborador('reenviar');
  const cambioPendiente = desactivar.isPending || reactivar.isPending;

  function onErrorEstatus(error: Error) {
    if (esApiError(error)) {
      toast.error(error.problem.title, {
        description: codigoSoporte(error),
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
    let porReasignar = 0;

    for (const e of empleados) {
      const motivos = getMotivosReasignacion(e, sucursalesMap, deptosMap, puestosMap);
      if (motivos.length > 0) {
        porReasignar++;
      }

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
      porReasignar,
    };
  }, [empleados, sucursalesMap, deptosMap, puestosMap]);

  // Lista filtrada
  const empleadosFiltrados = useMemo(() => {
    return empleados.filter((e) => {
      // Filtro de acceso / reasignación
      if (filtroAcceso === 'por-reasignar') {
        const motivos = getMotivosReasignacion(e, sucursalesMap, deptosMap, puestosMap);
        if (motivos.length === 0) return false;
      } else if (filtroAcceso === 'con-acceso' && e.usuarioId == null) return false;
      else if (filtroAcceso === 'sin-acceso' && e.usuarioId != null) return false;
      else if (filtroAcceso === 'acceso-inactivo') {
        if (e.usuarioId == null || e.usuarioActivo !== false) return false;
      } else if (filtroAcceso === 'sin-login') {
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
  }, [
    empleados,
    filtroAcceso,
    filtroSucursal,
    filtroDepto,
    busqueda,
    puestoClavePorId,
    sucursalesMap,
    deptosMap,
    puestosMap,
  ]);

  const filtroPagina = JSON.stringify([filtroAcceso, filtroSucursal, filtroDepto, busqueda]);
  const [filtroAnterior, setFiltroAnterior] = useState(filtroPagina);
  if (filtroAnterior !== filtroPagina) {
    setFiltroAnterior(filtroPagina);
    setOffset(0);
  }
  const offsetVisible = Math.min(
    offset,
    Math.max(0, Math.ceil(empleadosFiltrados.length / 10) - 1) * 10,
  );

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
    <div className="space-y-5">
      {/* Header */}
      <PageHeader
        titulo={<>Seguimiento y directorio de empleados</>}
        descripcion={
          <>
            Gestión de personal de talleres y plantas, estatus de cuentas ERP y aprovisionamiento en
            Microsoft Entra ID.
          </>
        }
        acciones={
          <>
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() => query.refetch()}
                disabled={query.isRefetching}
                title="Actualizar lista"
                aria-label="Actualizar lista"
              >
                <RefreshCw className={`h-4 w-4 ${query.isRefetching ? 'animate-spin' : ''}`} />
              </Button>
              {canGestionar && !agregando && (
                <Button
                  size="sm"
                  aria-label="Agregar empleado"
                  onClick={() => {
                    setCuentaParaAlta(null);
                    setAgregando(true);
                    setEditandoId(null);
                  }}
                >
                  <Plus className="mr-1.5 h-4 w-4" />
                  Agregar empleado
                </Button>
              )}
            </div>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <button
          type="button"
          className="text-left rounded-lg focus-visible:outline-brand"
          onClick={() => setFiltroAcceso('sin-login')}
        >
          <KpiCard etiqueta="Sin iniciar sesión" valor={metricas.sinLogin} tono="warning" />
        </button>
        <button
          type="button"
          className="text-left rounded-lg focus-visible:outline-brand"
          onClick={() => setFiltroAcceso('sin-acceso')}
        >
          <KpiCard etiqueta="Sin acceso ERP" valor={metricas.sinAcceso} tono="neutral" />
        </button>
        <button
          type="button"
          className="text-left rounded-lg focus-visible:outline-brand"
          onClick={() => setFiltroAcceso('acceso-inactivo')}
        >
          <KpiCard etiqueta="Acceso inactivo" valor={metricas.inactivos} tono="danger" />
        </button>
        <button
          type="button"
          className="text-left rounded-lg focus-visible:outline-brand"
          onClick={() => setFiltroAcceso('por-reasignar')}
        >
          <KpiCard etiqueta="Por reasignar" valor={metricas.porReasignar} tono="warning" />
        </button>
      </div>
      {/* Inline Form de Alta */}
      {agregando && canGestionar && (
        <EmpleadoInlineForm
          key={cuentaParaAlta?.objectId ?? 'nuevo'}
          cuentaExistente={
            cuentaParaAlta ? { nombre: cuentaParaAlta.nombre, correo: cuentaParaAlta.correo } : null
          }
          onCancel={() => {
            setAgregando(false);
            setCuentaParaAlta(null);
          }}
          onSaved={() => {
            setAgregando(false);
            setCuentaParaAlta(null);
          }}
        />
      )}

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 0, label: 'Con acceso ERP' },
              { valor: 1, label: 'Sin iniciar sesión' },
              { valor: 2, label: 'Sin acceso' },
              { valor: 3, label: 'Bloqueados' },
              { valor: 4, label: 'Por reasignar' },
              ...(canVerSinEmpleado
                ? [
                    {
                      valor: TAB_SIN_EMPLEADO,
                      label: `Cuentas sin empleado${cuentasSinEmpleado.data ? ` (${cuentasSinEmpleado.data.total})` : ''}`,
                    },
                  ]
                : []),
            ]}
            valor={
              vista === 'sin-empleado'
                ? TAB_SIN_EMPLEADO
                : filtroAcceso === 'todos'
                  ? undefined
                  : filtrosAcceso.indexOf(filtroAcceso)
            }
            conteos={undefined}
            onChange={(v) => {
              if (v === TAB_SIN_EMPLEADO) {
                setVista('sin-empleado');
                return;
              }
              setVista('empleados');
              setFiltroAcceso(v === undefined ? 'todos' : filtrosAcceso[v]);
            }}
          />
        }
        pie={
          vista === 'sin-empleado' ? undefined : (
            <PaginacionTabla
              total={empleadosFiltrados.length}
              offset={offsetVisible}
              limit={10}
              tamanos={[]}
              onChange={setOffset}
            />
          )
        }
        filtros={
          vista === 'sin-empleado' ? undefined : (
            <BarraFiltros
              filtros={
                <>
                  {/* Inputs de Búsqueda y Dropdowns */}
                  <div className="flex flex-wrap items-center gap-2 pt-0.5">
                    <div className="relative min-w-60 flex-1">
                      <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-ink-muted" />
                      <Input
                        placeholder="Buscar por nombre, clave, correo o puesto..."
                        value={busqueda}
                        onChange={(e) => setBusqueda(e.target.value)}
                        className="h-9 pl-9 pr-8 text-sm"
                      />
                      {busqueda && (
                        <button
                          type="button"
                          aria-label="Limpiar búsqueda"
                          onClick={() => setBusqueda('')}
                          className="absolute right-2.5 top-2.5 text-ink-muted hover:text-ink"
                        >
                          <X className="h-4 w-4" />
                        </button>
                      )}
                    </div>

                    {/* Filtro Sucursal */}
                    <select
                      value={filtroSucursal}
                      onChange={(e) => setFiltroSucursal(e.target.value)}
                      className="h-9 rounded-md border border-line-control bg-surface-page px-2.5 text-xs text-ink shadow-card-flat focus:outline-hidden focus:ring-1 focus:ring-brand"
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
                      className="h-9 rounded-md border border-line-control bg-surface-page px-2.5 text-xs text-ink shadow-card-flat focus:outline-hidden focus:ring-1 focus:ring-brand"
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
                </>
              }
            />
          )
        }
      >
        {vista === 'sin-empleado' ? (
          <CuentasSinEmpleado
            onCrearEmpleado={(c) => {
              setCuentaParaAlta(c);
              setAgregando(true);
              setEditandoId(null);
              window.scrollTo?.({ top: 0, behavior: 'smooth' });
            }}
          />
        ) : (
          <>
            {/* Banner informativo si está en filtro Por Reasignar */}
            {filtroAcceso === 'por-reasignar' && (
              <div className="rounded-lg border border-line bg-warning-bg/70 p-3.5 text-xs text-warning-fg    flex items-start gap-3">
                <AlertCircle className="h-5 w-5 text-warning-fg shrink-0 mt-0.5" />
                <div className="space-y-1">
                  <p className="font-semibold text-sm text-warning-fg ">
                    Colaboradores pendientes de reasignación
                  </p>
                  <p className="text-warning-fg  leading-relaxed">
                    Estos empleados tienen desvinculada o sin asignar su sucursal, departamento o
                    puesto (o la asignación se encuentra inactiva).
                    <strong className="font-semibold">
                      {' '}
                      Los colaboradores continúan operando con normalidad con su histórico
                    </strong>
                    , pero para asignarlos a nuevas solicitudes, viáticos o completar su perfil
                    operativo se requiere actualizar su asignación.
                  </p>
                </div>
              </div>
            )}

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
              <div className="rounded-lg border border-dashed bg-surface-muted/20 px-4 py-10 text-center">
                <Users className="mx-auto h-8 w-8 text-ink-muted/60" />
                <p className="mt-2 text-sm font-medium text-ink">
                  No se encontraron empleados con los filtros aplicados
                </p>
                <p className="text-xs text-ink-muted mt-0.5">
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
              <div className="overflow-hidden rounded-lg border bg-surface-card shadow-card-flat">
                <div className="border-b bg-surface-muted/30 px-4 py-2.5 text-xs font-semibold text-ink-muted flex items-center justify-between">
                  <span>
                    Mostrando {empleadosFiltrados.length} de {empleados.length} colaboradores
                  </span>
                  <span className="hidden sm:inline text-2xs text-ink-muted">
                    {metricas.sinLogin > 0
                      ? `${metricas.sinLogin} colaboradores pendientes de iniciar sesión`
                      : 'Todos los accesos activos han iniciado sesión'}
                  </span>
                </div>

                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Registro y estado</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {empleadosFiltrados.slice(offsetVisible, offsetVisible + 10).map((e) => {
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
                        tieneUsuario &&
                        !usuarioBloqueado &&
                        !pendientePrimerAcceso &&
                        !enProvision &&
                        !errorProvision;

                      const isReenviando = reenviandoId === e.id;
                      const motivosReasig = getMotivosReasignacion(
                        e,
                        sucursalesMap,
                        deptosMap,
                        puestosMap,
                      );

                      return (
                        <TableRow
                          key={e.id}
                          className="p-3.5 transition-colors hover:bg-surface-muted/15"
                        >
                          <TableCell>
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
                                    className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full border text-xs font-semibold ${getAvatarColors(
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
                                        className="font-semibold text-sm text-ink hover:text-brand hover:underline truncate"
                                      >
                                        {e.nombre}
                                      </Link>
                                      <span className="font-mono text-xs text-ink-muted bg-surface-muted/60 px-1.5 py-0.5 rounded">
                                        {e.clave}
                                      </span>
                                      {activo ? (
                                        <Badge
                                          variant={ESTADOS_CATALOGO[EstatusCatalogo.Activo].tono}
                                        >
                                          Activo
                                        </Badge>
                                      ) : (
                                        <Badge
                                          variant={ESTADOS_CATALOGO[EstatusCatalogo.Inactivo].tono}
                                        >
                                          Inactivo
                                        </Badge>
                                      )}
                                    </div>

                                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-ink-muted">
                                      {e.email && (
                                        <span className="flex items-center gap-1 truncate text-ink/80">
                                          <Mail className="h-3 w-3 text-ink-muted" />
                                          {e.email}
                                        </span>
                                      )}
                                      <span className="font-medium text-ink/90">
                                        {e.puestoNombre ||
                                          (e.puestoId
                                            ? (puestoClavePorId.get(e.puestoId) ?? '…')
                                            : 'Sin puesto')}
                                      </span>
                                      {e.departamentoNombre && (
                                        <span>· {e.departamentoNombre}</span>
                                      )}
                                      {!e.sucursalId && <Badge variant="info">Corporativo</Badge>}
                                      {e.sucursalNombre && (
                                        <span className="inline-flex items-center gap-1 font-medium">
                                          <Building2 className="h-3 w-3 text-ink-muted" />
                                          {e.sucursalNombre}
                                          {e.sucursalTipo === TipoSucursal.Planta ? (
                                            <span className="text-2xs font-semibold text-info-fg bg-info-bg border border-line px-1.5 py-0.2 rounded">
                                              Planta
                                            </span>
                                          ) : (
                                            <span className="text-2xs font-semibold text-info-fg bg-info-bg border border-line px-1.5 py-0.2 rounded">
                                              Taller
                                            </span>
                                          )}
                                        </span>
                                      )}
                                    </div>
                                    {motivosReasig.length > 0 && (
                                      <div className="flex flex-wrap items-center gap-1.5 pt-0.5">
                                        <span className="text-2xs font-medium text-warning-fg ">
                                          Pendiente de reasignar:
                                        </span>
                                        {motivosReasig.map((m) => (
                                          <Badge key={m} variant="warning">
                                            {m}
                                          </Badge>
                                        ))}
                                      </div>
                                    )}
                                  </div>
                                </div>

                                {/* Estado de Acceso ERP & Acciones Rápidas */}
                                <div className="flex flex-wrap items-center gap-2.5 sm:self-center">
                                  {/* Badge de Acceso ERP */}
                                  <div className="flex flex-col items-start sm:items-end min-w-42.5">
                                    {accesoActivo && (
                                      <Badge variant={ESTADOS_ACCESO.activo.tono}>
                                        {ESTADOS_ACCESO.activo.texto}
                                      </Badge>
                                    )}

                                    {pendientePrimerAcceso && (
                                      <Badge variant={ESTADOS_ACCESO.pendiente.tono}>
                                        {ESTADOS_ACCESO.pendiente.texto}
                                      </Badge>
                                    )}

                                    {usuarioBloqueado && (
                                      <Badge variant={ESTADOS_ACCESO.bloqueado.tono}>
                                        {ESTADOS_ACCESO.bloqueado.texto}
                                      </Badge>
                                    )}

                                    {enProvision && (
                                      <Badge variant={ESTADOS_ACCESO.provision.tono}>
                                        {ESTADOS_ACCESO.provision.texto}
                                      </Badge>
                                    )}

                                    {errorProvision && (
                                      <Badge variant={ESTADOS_ACCESO.error.tono}>
                                        {ESTADOS_ACCESO.error.texto}
                                      </Badge>
                                    )}

                                    {!tieneUsuario && (
                                      <Badge variant={ESTADOS_ACCESO.sinAcceso.tono}>
                                        {ESTADOS_ACCESO.sinAcceso.texto}
                                      </Badge>
                                    )}

                                    {/* Subtexto descriptivo del acceso */}
                                    <div className="text-2xs text-ink-muted mt-0.5 truncate max-w-50">
                                      {accesoActivo && e.primerAccesoEn && (
                                        <span>1er login: {formatFecha(e.primerAccesoEn)}</span>
                                      )}
                                      {pendientePrimerAcceso && (
                                        <span
                                          title={`Destino: ${e.emailContacto ?? 'correo personal'}`}
                                        >
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
                                        className="h-8 border-line bg-warning-bg text-warning-fg hover:bg-warning-bg shadow-card-flat font-medium text-xs gap-1"
                                        disabled={isReenviando || reenviar.isPending}
                                        onClick={() => handleReenviarAcceso(e)}
                                        title="Reenviar las credenciales al correo de contacto"
                                      >
                                        <Send
                                          className={`h-3.5 w-3.5 text-warning-fg ${
                                            isReenviando ? 'animate-pulse' : ''
                                          }`}
                                        />
                                        {isReenviando ? 'Reenviando…' : 'Reenviar acceso'}
                                      </Button>
                                    )}

                                    {/* Botón Dar Acceso si no tiene cuenta */}
                                    {!tieneUsuario && canGestionar && canDarAcceso && activo && (
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
                                          accesoId === e.id ? 'bg-surface-muted text-ink' : ''
                                        }`}
                                        onClick={() => setAccesoId(accesoId === e.id ? null : e.id)}
                                        title={`Gestionar cuenta de ${e.nombre}`}
                                      >
                                        <KeyRound className="h-3.5 w-3.5 mr-1" />
                                        Acceso
                                      </Button>
                                    )}

                                    {/* Botón Reasignar si tiene asignación pendiente */}
                                    {canGestionar && motivosReasig.length > 0 && (
                                      <Button
                                        size="sm"
                                        variant="outline"
                                        className="h-8 text-xs gap-1 border-line bg-warning-bg/60 text-warning-fg hover:bg-warning-bg hover:text-warning-fg   "
                                        onClick={() => {
                                          setEditandoId(e.id);
                                          setAgregando(false);
                                        }}
                                        title={`Reasignar sucursal, departamento o puesto de ${e.nombre}`}
                                      >
                                        <ArrowRightLeft className="h-3.5 w-3.5" />
                                        Reasignar
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
                                    {canGestionar &&
                                      (activo ? (
                                        <Button
                                          variant="ghost"
                                          size="sm"
                                          className="h-8 w-8 p-0 text-ink-muted hover:text-danger-fg"
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
                                          className="h-8 w-8 p-0 text-success-fg hover:text-success-fg"
                                          aria-label={`Reactivar empleado ${e.clave}`}
                                          disabled={cambioPendiente}
                                          onClick={() =>
                                            reactivar.mutate(
                                              { id: e.id, idempotencyKey: crypto.randomUUID() },
                                              {
                                                onSuccess: () =>
                                                  toast.success(
                                                    `Empleado "${e.nombre}" reactivado`,
                                                  ),
                                                onError: onErrorEstatus,
                                              },
                                            )
                                          }
                                          title={`Reactivar empleado ${e.clave}`}
                                        >
                                          <Power className="h-3.5 w-3.5" />
                                        </Button>
                                      ))}
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
                                  emailContacto={e.emailContacto}
                                  empleadoActivo={activo}
                                  sucursalId={e.sucursalId}
                                  departamentoId={e.departamentoId}
                                  puestoId={e.puestoId}
                                />
                              </div>
                            )}
                          </TableCell>
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              </div>
            )}

            {/* Modal de Confirmación para Desactivar Empleado */}
          </>
        )}
      </ListadoModulo>
      <AlertDialog
        open={confirmDesactivar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader className="px-6 pt-5 pb-3">
            <AlertDialogTitle>Desactivar empleado</AlertDialogTitle>
          </AlertDialogHeader>
          <DialogBody>
            <AlertDialogDescription>
              ¿Confirmas dar de baja a{' '}
              <span className="font-semibold text-ink">{confirmDesactivar?.nombre}</span>? Saldrá de
              los selectores operativos (viáticos, comprobaciones); su historial se conserva. Si
              tenía acceso al ERP, su usuario quedará bloqueado. En una recontratación, el acceso se
              reactiva por separado.
            </AlertDialogDescription>
          </DialogBody>
          <AlertDialogFooter className="border-t border-line-divider bg-surface-subtle px-6 py-4">
            <AlertDialogCancel disabled={cambioPendiente}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivar == null) return;
                desactivar.mutate(
                  { id: confirmDesactivar.id, idempotencyKey: crypto.randomUUID() },
                  {
                    onSuccess: () => {
                      toast.success(`Empleado "${confirmDesactivar.nombre}" desactivado`);
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
