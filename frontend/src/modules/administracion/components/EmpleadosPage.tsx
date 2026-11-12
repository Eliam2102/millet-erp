import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogBody,
} from '@/components/ui/dialog';
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
import { ESTADOS_ACCESO } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import { Fragment, useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { TriangleAlert, MoreHorizontal, Plus, RefreshCw, Users } from 'lucide-react';
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
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasAllPermissions, useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EmpleadoInlineForm } from '@/modules/administracion/components/EmpleadoInlineForm';
import { EmpleadoAccesoPanel } from '@/modules/administracion/components/EmpleadoAccesoPanel';
import { LigarCuentaForm } from '@/modules/administracion/components/CuentasSinEmpleado';
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
} from '@/components/ui/dropdown-menu';
import {
  useCuentasSinEmpleado,
  useCuentasTecnicas,
  type CuentaSinEmpleado,
  type CuentaTecnica,
} from '@/modules/administracion/api/empleados';

export type FiltroAcceso =
  | 'todos'
  | 'con-acceso'
  | 'sin-acceso'
  | 'sin-login'
  | 'acceso-inactivo'
  | 'por-reasignar';

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
export type VistaEmpleados = 'empleados' | 'sin-empleado' | 'tecnicas';

/** Índice de la pestaña «Cuentas sin empleado» en <c>TabsEstado</c>. */
const TAB_SIN_EMPLEADO = 5;
/** Índice de la pestaña «Cuentas técnicas» (86e3yxe9m). */
const TAB_TECNICAS = 6;

export function EmpleadosPage({ vistaInicial = 'empleados' }: { vistaInicial?: VistaEmpleados }) {
  const [offset, setOffset] = useState(0);
  const [cuentaParaLigar, setCuentaParaLigar] = useState<CuentaSinEmpleado | null>(null);
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
  const buscarCuentas = useDebouncedValue(busqueda.trim(), 200);
  const cuentasSinEmpleado = useCuentasSinEmpleado(buscarCuentas, canVerSinEmpleado);
  const cuentasTecnicas = useCuentasTecnicas(buscarCuentas, canVerSinEmpleado);
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
      toast.error(
        'No se pudo actualizar el acceso del empleado. Revisa los datos y tu conexión antes de volver a intentarlo.',
      );
    } else {
      toast.error('Error al actualizar el empleado.');
    }
    setConfirmDesactivar(null);
  }

  const empleadosBase = empleados.filter((e) => {
    if (filtroSucursal !== 'todas' && e.sucursalId !== filtroSucursal) return false;
    if (filtroDepto !== 'todos' && e.departamentoId !== filtroDepto) return false;
    const texto = busqueda.trim().toLocaleLowerCase('es-MX');
    return [
      e.nombre,
      e.clave,
      e.email,
      e.puestoNombre,
      e.puestoId ? puestoClavePorId.get(e.puestoId) : null,
    ].some((valor) => valor?.toLocaleLowerCase('es-MX').includes(texto));
  });
  function coincideAcceso(e: EmpleadoListItem, filtro: FiltroAcceso) {
    switch (filtro) {
      case 'con-acceso':
        return e.usuarioId != null;
      case 'sin-acceso':
        return e.usuarioId == null;
      case 'sin-login':
        return (
          e.usuarioId != null &&
          e.usuarioActivo !== false &&
          (e.primerAccesoEn == null || e.estadoAcceso === 1)
        );
      case 'acceso-inactivo':
        return e.usuarioId != null && e.usuarioActivo === false;
      case 'por-reasignar':
        return getMotivosReasignacion(e, sucursalesMap, deptosMap, puestosMap).length > 0;
      default:
        return true;
    }
  }
  const ocultarCuentas = filtroSucursal !== 'todas' || filtroDepto !== 'todos';
  const textoCuenta = busqueda.trim().toLocaleLowerCase('es-MX');
  function coincideCuenta(c: { nombre: string; correo: string }) {
    return (
      !ocultarCuentas &&
      [c.nombre, c.correo].some((valor) => valor.toLocaleLowerCase('es-MX').includes(textoCuenta))
    );
  }
  const sinEmpleado = canVerSinEmpleado
    ? (cuentasSinEmpleado.data?.items ?? []).filter(coincideCuenta)
    : [];
  const tecnicas = canVerSinEmpleado
    ? (cuentasTecnicas.data?.items ?? []).filter(coincideCuenta)
    : [];
  const conteosEmpleados = filtrosAcceso.map(
    (filtro) => empleadosBase.filter((e) => coincideAcceso(e, filtro)).length,
  );
  const metricas = {
    sinLogin: conteosEmpleados[1],
    sinAcceso: conteosEmpleados[2],
    inactivos: conteosEmpleados[3],
    porReasignar: conteosEmpleados[4],
  };
  const cargando =
    query.isLoading ||
    sucursalesQuery.isLoading ||
    deptosQuery.isLoading ||
    puestosQuery.isLoading ||
    (canVerSinEmpleado &&
      (buscarCuentas !== busqueda.trim() ||
        cuentasSinEmpleado.isLoading ||
        cuentasTecnicas.isLoading));
  const errorConsulta = query.isError
    ? query
    : canVerSinEmpleado && cuentasSinEmpleado.isError
      ? cuentasSinEmpleado
      : canVerSinEmpleado && cuentasTecnicas.isError
        ? cuentasTecnicas
        : null;
  const conteos =
    cargando || errorConsulta
      ? undefined
      : {
          ...Object.fromEntries(conteosEmpleados.map((cantidad, indice) => [indice, cantidad])),
          [TAB_SIN_EMPLEADO]: sinEmpleado.length,
          [TAB_TECNICAS]: tecnicas.length,
        };
  const total = empleadosBase.length + sinEmpleado.length + tecnicas.length;
  type Registro =
    | { tipo: 'empleado'; dato: EmpleadoListItem }
    | { tipo: 'sin-empleado'; dato: CuentaSinEmpleado }
    | { tipo: 'tecnica'; dato: CuentaTecnica };
  const registros: Registro[] =
    vista === 'sin-empleado'
      ? sinEmpleado.map((dato) => ({ tipo: 'sin-empleado', dato }))
      : vista === 'tecnicas'
        ? tecnicas.map((dato) => ({ tipo: 'tecnica', dato }))
        : [
            ...empleadosBase
              .filter((e) => coincideAcceso(e, filtroAcceso))
              .map((dato): Registro => ({ tipo: 'empleado', dato })),
            ...(filtroAcceso === 'todos'
              ? sinEmpleado.map((dato): Registro => ({ tipo: 'sin-empleado', dato }))
              : []),
            ...(filtroAcceso === 'todos'
              ? tecnicas.map((dato): Registro => ({ tipo: 'tecnica', dato }))
              : []),
          ];
  const filtroPagina = JSON.stringify([vista, filtroAcceso, filtroSucursal, filtroDepto, busqueda]);
  const [filtroAnterior, setFiltroAnterior] = useState(filtroPagina);
  if (filtroAnterior !== filtroPagina) {
    setFiltroAnterior(filtroPagina);
    setOffset(0);
  }
  const offsetVisible = Math.min(offset, Math.max(0, Math.ceil(registros.length / 10) - 1) * 10);
  function seleccionarFiltro(filtro: FiltroAcceso) {
    setVista('empleados');
    setFiltroAcceso(filtro);
  }
  const columnas = 'grid grid-cols-[minmax(240px,1.6fr)_150px_150px_140px_150px_136px_124px_36px]';

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
            toast.error(
              'No se pudo actualizar el acceso del empleado. Revisa los datos y tu conexión antes de volver a intentarlo.',
            );
          } else {
            toast.error('No se pudo reenviar el acceso.');
          }
          setReenviandoId(null);
        },
      },
    );
  }

  return (
    <div className="flex h-[calc(100dvh-7.5rem)] min-h-0 min-w-0 flex-1 flex-col gap-5 overflow-hidden">
      {/* Header */}
      <div className="shrink-0">
        <PageHeader
          titulo={<>Seguimiento y directorio de empleados</>}
          descripcion={
            <>Consulta y administra empleados, sus asignaciones y su acceso al ERP con Microsoft.</>
          }
          acciones={
            <>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    void query.refetch();
                    if (canVerSinEmpleado) {
                      void cuentasSinEmpleado.refetch();
                      void cuentasTecnicas.refetch();
                    }
                  }}
                  disabled={
                    query.isRefetching ||
                    (canVerSinEmpleado &&
                      (cuentasSinEmpleado.isRefetching || cuentasTecnicas.isRefetching))
                  }
                  title={
                    query.isRefetching
                      ? 'Actualizando la lista; espera a que termine.'
                      : 'Actualizar lista'
                  }
                  aria-label="Actualizar lista"
                >
                  <RefreshCw
                    strokeWidth={1.6}
                    className={`h-3.5 w-3.5 ${query.isRefetching ? 'animate-spin' : ''}`}
                  />
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
                    <Plus className="mr-1.5 h-3.5 w-3.5" strokeWidth={1.6} />
                    Agregar empleado
                  </Button>
                )}
              </div>
            </>
          }
        />
      </div>
      <div className="grid shrink-0 grid-cols-2 gap-3 lg:grid-cols-4">
        <button
          type="button"
          className="text-left rounded-lg focus-visible:outline-brand"
          onClick={() => seleccionarFiltro('sin-login')}
        >
          <KpiCard
            etiqueta="Sin iniciar sesión"
            valor={
              !cargando && !errorConsulta ? (
                metricas.sinLogin
              ) : (
                <span className="text-ink-muted">—</span>
              )
            }
            tono="warning"
          />
        </button>
        <button
          type="button"
          className="text-left rounded-lg focus-visible:outline-brand"
          onClick={() => seleccionarFiltro('sin-acceso')}
        >
          <KpiCard
            etiqueta="Sin acceso ERP"
            valor={
              !cargando && !errorConsulta ? (
                metricas.sinAcceso
              ) : (
                <span className="text-ink-muted">—</span>
              )
            }
            tono="neutral"
          />
        </button>
        <button
          type="button"
          className="text-left rounded-lg focus-visible:outline-brand"
          onClick={() => seleccionarFiltro('acceso-inactivo')}
        >
          <KpiCard
            etiqueta="Acceso bloqueado"
            valor={
              !cargando && !errorConsulta ? (
                metricas.inactivos
              ) : (
                <span className="text-ink-muted">—</span>
              )
            }
            tono="danger"
          />
        </button>
        <button
          type="button"
          className="text-left rounded-lg focus-visible:outline-brand"
          onClick={() => seleccionarFiltro('por-reasignar')}
        >
          <KpiCard
            etiqueta="Por reasignar"
            valor={
              !cargando && !errorConsulta ? (
                metricas.porReasignar
              ) : (
                <span className="text-ink-muted">—</span>
              )
            }
            tono="warning"
          />
        </button>
      </div>
      {/* Inline Form de Alta */}
      {agregando && canGestionar && (
        <div className="max-h-[50dvh] shrink-0 overflow-auto">
          <EmpleadoInlineForm
            key={cuentaParaAlta?.objectId ?? 'nuevo'}
            cuentaExistente={
              cuentaParaAlta
                ? { nombre: cuentaParaAlta.nombre, correo: cuentaParaAlta.correo }
                : null
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
        </div>
      )}

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={[
              { valor: 0, label: 'Con acceso ERP' },
              { valor: 1, label: 'Sin iniciar sesión' },
              { valor: 2, label: 'Sin acceso' },
              { valor: 3, label: 'Acceso bloqueado' },
              { valor: 4, label: 'Por reasignar' },
              ...(canVerSinEmpleado
                ? [
                    {
                      valor: TAB_SIN_EMPLEADO,
                      label: 'Cuentas sin empleado',
                    },
                    {
                      valor: TAB_TECNICAS,
                      label: 'Cuentas técnicas',
                    },
                  ]
                : []),
            ]}
            valor={
              vista === 'sin-empleado'
                ? TAB_SIN_EMPLEADO
                : vista === 'tecnicas'
                  ? TAB_TECNICAS
                  : filtroAcceso === 'todos'
                    ? undefined
                    : filtrosAcceso.indexOf(filtroAcceso)
            }
            conteos={conteos}
            total={conteos ? total : undefined}
            onChange={(v) => {
              if (v === TAB_SIN_EMPLEADO) {
                setVista('sin-empleado');
                return;
              }
              if (v === TAB_TECNICAS) {
                setVista('tecnicas');
                return;
              }
              setVista('empleados');
              setFiltroAcceso(v === undefined ? 'todos' : filtrosAcceso[v]);
            }}
          />
        }
        pie={
          cargando || errorConsulta ? undefined : (
            <PaginacionTabla
              total={registros.length}
              offset={offsetVisible}
              limit={10}
              tamanos={[]}
              onChange={setOffset}
            />
          )
        }
        filtros={
          <div className="space-y-2">
            <BarraFiltros
              total={
                cargando
                  ? 'Cargando registros…'
                  : errorConsulta
                    ? 'Consulta incompleta'
                    : `${registros.length} registros`
              }
              busqueda={
                <Input
                  aria-label="Buscar empleados"
                  placeholder="Nombre, clave, correo o puesto"
                  value={busqueda}
                  onChange={(e) => setBusqueda(e.target.value)}
                />
              }
              filtros={
                <>
                  <select
                    aria-label="Filtrar por sucursal"
                    value={filtroSucursal}
                    onChange={(e) => setFiltroSucursal(e.target.value)}
                    className="h-ctl-md max-w-48 rounded-md border border-line-control bg-surface-subtle px-2.5 text-xs"
                  >
                    <option value="todas">Todas las sucursales</option>
                    {sucursales.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.nombre} ({s.tipo === TipoSucursal.Planta ? 'Planta' : 'Taller'})
                      </option>
                    ))}
                  </select>
                  <select
                    aria-label="Filtrar por departamento"
                    value={filtroDepto}
                    onChange={(e) => setFiltroDepto(e.target.value)}
                    className="h-ctl-md max-w-48 rounded-md border border-line-control bg-surface-subtle px-2.5 text-xs"
                  >
                    <option value="todos">Todos los departamentos</option>
                    {departamentos.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.nombre}
                      </option>
                    ))}
                  </select>
                </>
              }
              onLimpiar={
                busqueda || filtroSucursal !== 'todas' || filtroDepto !== 'todos'
                  ? () => {
                      setBusqueda('');
                      setFiltroSucursal('todas');
                      setFiltroDepto('todos');
                    }
                  : undefined
              }
            />
            {/* shortcut: el API de cuentas limita cada consulta a 50 sin offset; paginar en servidor cuando se amplíe su contrato. */}
            {canVerSinEmpleado &&
              !ocultarCuentas &&
              !cargando &&
              ((cuentasSinEmpleado.data?.total ?? 0) >
                (cuentasSinEmpleado.data?.items.length ?? 0) ||
                (cuentasTecnicas.data?.total ?? 0) > (cuentasTecnicas.data?.items.length ?? 0)) && (
                <p className="text-xs text-warning-fg">
                  La consulta de cuentas alcanzó su límite. Busca por nombre o correo para encontrar
                  las demás.
                </p>
              )}
            {canVerSinEmpleado && (
              <p className="text-xs text-ink-muted">
                Los filtros de sucursal y departamento ocultan las cuentas sin asignación.
              </p>
            )}
          </div>
        }
      >
        <div className="flex min-h-full flex-col">
          {vista === 'empleados' && filtroAcceso === 'por-reasignar' && (
            <div
              role="note"
              className="flex shrink-0 items-start gap-3 rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg"
            >
              <TriangleAlert className="h-4 w-4 shrink-0" strokeWidth={1.6} />
              <div>
                <p className="font-medium">Colaboradores pendientes de reasignación</p>
                <p className="text-xs">
                  Los colaboradores continúan operando con normalidad con su histórico. Asigna una
                  sucursal, departamento y puesto activos para completar su perfil operativo.
                </p>
              </div>
            </div>
          )}
          {errorConsulta ? (
            <ErrorState
              title={
                query.isError
                  ? 'No se pudieron cargar los empleados'
                  : 'No se pudieron consultar las cuentas'
              }
              problem={problemaConsulta(errorConsulta.error)}
              onRetry={() => errorConsulta.refetch()}
            />
          ) : cargando ? (
            <TableSkeleton rows={6} />
          ) : registros.length === 0 ? (
            <div className="flex flex-1 flex-col items-center justify-center gap-2 px-4 py-10 text-center">
              <Users className="h-8 w-8 text-ink-muted" strokeWidth={1.6} />
              <p className="font-medium">No se encontraron registros con los filtros aplicados</p>
              <p className="text-xs text-ink-muted">
                Cambia la búsqueda o los filtros para consultar otros registros.
              </p>
            </div>
          ) : (
            <Table className="block min-w-[1126px]">
              <TableHeader className="block">
                <TableRow className={columnas}>
                  {[
                    'Empleado',
                    'Tipo',
                    'Puesto',
                    'Departamento',
                    'Sucursal',
                    'Acceso',
                    'Último acceso',
                    '',
                  ].map((titulo) => (
                    <TableHead key={titulo}>
                      {titulo || <span className="sr-only">Acciones</span>}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody className="block">
                {registros.slice(offsetVisible, offsetVisible + 10).map((registro) => {
                  if (registro.tipo !== 'empleado') {
                    const c = registro.dato;
                    const tecnica = registro.tipo === 'tecnica' ? registro.dato : null;
                    const sinFicha = registro.tipo === 'sin-empleado' ? registro.dato : null;
                    const habilitada =
                      registro.tipo === 'tecnica' ? registro.dato.activo : registro.dato.habilitada;
                    return (
                      <TableRow
                        key={`${registro.tipo}-${tecnica?.usuarioId ?? sinFicha?.objectId}`}
                        className={`${columnas} h-12 items-center`}
                      >
                        <TableCell className="min-w-0 py-0">
                          <p className="truncate font-medium">{c.nombre}</p>
                          <p className="truncate text-xs text-ink-muted">{c.correo}</p>
                        </TableCell>
                        <TableCell>
                          <Badge variant="neutral">
                            {tecnica ? 'Cuenta técnica' : 'Cuenta sin empleado'}
                          </Badge>
                        </TableCell>
                        <TableCell className="text-ink-muted">Sin puesto</TableCell>
                        <TableCell className="text-ink-muted">Sin asignar</TableCell>
                        <TableCell className="text-ink-muted">Sin sucursal</TableCell>
                        <TableCell>
                          <Badge
                            variant={
                              !habilitada
                                ? 'danger'
                                : tecnica || sinFicha?.yaEntroAlErp
                                  ? 'success'
                                  : 'warning'
                            }
                          >
                            {!habilitada
                              ? 'Acceso bloqueado'
                              : tecnica || sinFicha?.yaEntroAlErp
                                ? 'Acceso activo'
                                : 'Sin iniciar sesión'}
                          </Badge>
                        </TableCell>
                        <TableCell
                          className="text-ink-muted"
                          title="El directorio no registra el último acceso"
                        >
                          —
                        </TableCell>
                        <TableCell className="px-0">
                          <DropdownMenu>
                            <DropdownMenuTrigger asChild>
                              <Button
                                variant="ghost"
                                size="icon"
                                className="h-7 w-7"
                                aria-label={`Acciones de ${c.nombre}`}
                              >
                                <MoreHorizontal className="h-4 w-4" strokeWidth={1.6} />
                              </Button>
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="end">
                              {c.usuarioId && (
                                <DropdownMenuItem asChild>
                                  <Link to="/admin/usuarios/$id" params={{ id: c.usuarioId }}>
                                    Ver cuenta
                                  </Link>
                                </DropdownMenuItem>
                              )}
                              {tecnica && (
                                <>
                                  <DropdownMenuLabel>
                                    Rol: {tecnica.roles.join(', ') || 'Sin rol'}
                                  </DropdownMenuLabel>
                                  <DropdownMenuLabel>
                                    Primer acceso:{' '}
                                    <DateTimeDisplay
                                      value={tecnica.primerAccesoEn}
                                      variant="corta"
                                    />
                                  </DropdownMenuLabel>
                                </>
                              )}
                              {sinFicha && !c.usuarioId && (
                                <DropdownMenuLabel>
                                  Aún no entra al ERP: no tiene rol. Se asigna al crear su empleado.
                                </DropdownMenuLabel>
                              )}
                              {sinFicha && habilitada && (
                                <>
                                  <DropdownMenuItem
                                    onSelect={() => {
                                      setCuentaParaAlta(sinFicha);
                                      setAgregando(true);
                                      setEditandoId(null);
                                    }}
                                  >
                                    Dar de alta como empleado
                                  </DropdownMenuItem>
                                  <DropdownMenuItem onSelect={() => setCuentaParaLigar(sinFicha)}>
                                    Ligar a empleado existente
                                  </DropdownMenuItem>
                                </>
                              )}
                              {sinFicha && !habilitada && (
                                <DropdownMenuLabel>
                                  Cuenta deshabilitada en Microsoft
                                </DropdownMenuLabel>
                              )}
                            </DropdownMenuContent>
                          </DropdownMenu>
                        </TableCell>
                      </TableRow>
                    );
                  }
                  const e = registro.dato;
                  const activo = e.estatus === EstatusCatalogo.Activo;
                  const tieneUsuario = e.usuarioId != null;
                  const pendiente = coincideAcceso(e, 'sin-login');
                  const estado = !tieneUsuario
                    ? ESTADOS_ACCESO.sinAcceso
                    : e.usuarioActivo === false
                      ? ESTADOS_ACCESO.bloqueado
                      : e.estadoAcceso === 2
                        ? ESTADOS_ACCESO.provision
                        : e.estadoAcceso === 3
                          ? ESTADOS_ACCESO.error
                          : pendiente
                            ? ESTADOS_ACCESO.pendiente
                            : ESTADOS_ACCESO.activo;
                  const motivos = getMotivosReasignacion(e, sucursalesMap, deptosMap, puestosMap);
                  return (
                    <Fragment key={e.id}>
                      <TableRow
                        className={`${columnas} h-12 items-center hover:bg-surface-row-focus`}
                      >
                        <TableCell className="min-w-0 py-0">
                          <Link
                            to="/admin/empleados/$id"
                            params={{ id: e.id }}
                            className="block truncate font-medium text-brand"
                          >
                            {e.nombre}
                          </Link>
                          <p className="truncate text-xs text-ink-muted">
                            <span className="font-mono">{e.clave}</span> · {e.email || 'Sin correo'}
                            {!activo && ' · Inactivo'}
                          </p>
                        </TableCell>
                        <TableCell>
                          <Badge variant="neutral">Empleado</Badge>
                        </TableCell>
                        <TableCell className="truncate" title={e.puestoNombre ?? undefined}>
                          {e.puestoNombre ||
                            (e.puestoId ? puestoClavePorId.get(e.puestoId) : null) ||
                            'Sin puesto'}
                        </TableCell>
                        <TableCell className="truncate" title={motivos.join(' · ')}>
                          {e.departamentoNombre || 'Sin asignar'}
                        </TableCell>
                        <TableCell className="truncate">
                          {e.sucursalNombre || 'Sin sucursal'}
                        </TableCell>
                        <TableCell>
                          <Badge variant={estado.tono}>{estado.texto}</Badge>
                        </TableCell>
                        <TableCell
                          className="text-ink-muted"
                          title="El directorio no registra el último acceso"
                        >
                          —
                        </TableCell>
                        <TableCell className="px-0">
                          <DropdownMenu>
                            <DropdownMenuTrigger asChild>
                              <Button
                                variant="ghost"
                                size="icon"
                                className="h-7 w-7"
                                aria-label={`Acciones de ${e.nombre}`}
                              >
                                <MoreHorizontal className="h-4 w-4" strokeWidth={1.6} />
                              </Button>
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="end">
                              <DropdownMenuLabel>
                                {activo ? 'Empleado activo' : 'Empleado inactivo'}
                              </DropdownMenuLabel>
                              {motivos.length > 0 && (
                                <DropdownMenuLabel>
                                  Pendiente de reasignar: {motivos.join(' · ')}
                                </DropdownMenuLabel>
                              )}
                              {e.primerAccesoEn && (
                                <DropdownMenuLabel>
                                  Primer acceso:{' '}
                                  <DateTimeDisplay value={e.primerAccesoEn} variant="corta" />
                                </DropdownMenuLabel>
                              )}
                              {pendiente && canCrearUsuarios && activo && (
                                <DropdownMenuItem
                                  disabled={reenviandoId === e.id || reenviar.isPending}
                                  onSelect={() => handleReenviarAcceso(e)}
                                >
                                  Reenviar acceso
                                </DropdownMenuItem>
                              )}
                              {canGestionar && (tieneUsuario || (canDarAcceso && activo)) && (
                                <DropdownMenuItem
                                  onSelect={() => setAccesoId(accesoId === e.id ? null : e.id)}
                                >
                                  {tieneUsuario ? 'Gestionar acceso' : 'Dar acceso'}
                                </DropdownMenuItem>
                              )}
                              {canGestionar && (
                                <>
                                  <DropdownMenuItem
                                    onSelect={() => {
                                      setEditandoId(e.id);
                                      setAgregando(false);
                                    }}
                                  >
                                    {motivos.length ? 'Reasignar empleado' : 'Editar empleado'}{' '}
                                    {e.clave}
                                  </DropdownMenuItem>
                                  <DropdownMenuItem
                                    disabled={cambioPendiente}
                                    onSelect={() =>
                                      activo
                                        ? setConfirmDesactivar(e)
                                        : reactivar.mutate(
                                            { id: e.id, idempotencyKey: crypto.randomUUID() },
                                            {
                                              onSuccess: () =>
                                                toast.success(`Empleado "${e.nombre}" reactivado`),
                                              onError: onErrorEstatus,
                                            },
                                          )
                                    }
                                  >
                                    {activo ? 'Desactivar' : 'Reactivar'} empleado {e.clave}
                                  </DropdownMenuItem>
                                </>
                              )}
                            </DropdownMenuContent>
                          </DropdownMenu>
                        </TableCell>
                      </TableRow>
                      {editandoId === e.id && canGestionar && (
                        <TableRow>
                          <TableCell colSpan={8}>
                            <EmpleadoInlineForm
                              empleado={e}
                              onCancel={() => setEditandoId(null)}
                              onSaved={() => setEditandoId(null)}
                            />
                          </TableCell>
                        </TableRow>
                      )}
                      {accesoId === e.id && (
                        <TableRow>
                          <TableCell colSpan={8}>
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
                          </TableCell>
                        </TableRow>
                      )}
                    </Fragment>
                  );
                })}
              </TableBody>
            </Table>
          )}
        </div>
      </ListadoModulo>
      <Dialog
        open={cuentaParaLigar != null}
        onOpenChange={(open) => {
          if (!open) setCuentaParaLigar(null);
        }}
      >
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>Ligar cuenta a empleado</DialogTitle>
            <DialogDescription>
              Selecciona un empleado activo sin cuenta y su rol en la empresa.
            </DialogDescription>
          </DialogHeader>
          <DialogBody>
            {cuentaParaLigar && canVerSinEmpleado && (
              <LigarCuentaForm cuenta={cuentaParaLigar} onCerrar={() => setCuentaParaLigar(null)} />
            )}
          </DialogBody>
        </DialogContent>
      </Dialog>
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
