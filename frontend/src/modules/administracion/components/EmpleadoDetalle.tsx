import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useDepartamentos, usePuestos, useSucursales } from '@/features/catalogos/api';
import { useUsuario } from '@/modules/identidad/api/usuarios';
import { RolesPorEmpresaPanel } from '@/modules/identidad/components/RolesPorEmpresaPanel';
import { EmpleadoInlineForm } from './EmpleadoInlineForm';
import { EmpleadoAccesoPanel } from './EmpleadoAccesoPanel';
import { useEmpleadosAdmin } from '@/modules/administracion/api/empleados';
import {
  useAsignarUsuarioASucursal,
  useDesactivarAsignacionUsuarioSucursal,
  useReactivarAsignacionUsuarioSucursal,
  useSucursalesDeUsuario,
} from '@/modules/administracion/api/sucursal-usuarios';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

type Tab = 'datos' | 'puesto' | 'sucursales' | 'acceso';

/** Ficha única del colaborador: perfil laboral, ámbito operativo y acceso ERP. */
export function EmpleadoDetalle() {
  const { id } = useParams({ from: '/_app/admin/empleados/$id' });
  const [tab, setTab] = useState<Tab>('datos');
  const [editando, setEditando] = useState(false);
  const [nuevaSucursal, setNuevaSucursal] = useState('');
  const empleados = useEmpleadosAdmin();
  const sucursales = useSucursales();
  const departamentos = useDepartamentos();
  const puestos = usePuestos();
  const empleado = empleados.data?.find((e) => e.id === id);
  const puedeLeerUsuarios = useHasPermission(PermisosCanonicos.IdentidadUsuariosLeer);
  const usuario = useUsuario(puedeLeerUsuarios ? empleado?.usuarioId : null);
  const puedeGestionarSucursales = useHasPermission(PermisosCanonicos.AdminSucursalesUsuariosGestionar);
  const asignaciones = useSucursalesDeUsuario(puedeGestionarSucursales ? empleado?.usuarioId ?? null : null);
  const asignar = useAsignarUsuarioASucursal();
  const desactivar = useDesactivarAsignacionUsuarioSucursal();
  const reactivar = useReactivarAsignacionUsuarioSucursal();
  const error = (err: Error) => toast.error(esApiError(err) ? err.problem.title : 'No se pudo actualizar la sucursal.');

  if (empleados.isLoading) return <div className="p-4"><TableSkeleton rows={5} /></div>;
  if (empleados.isError) return <ErrorState title="No se pudo consultar el empleado" onRetry={() => empleados.refetch()} />;
  if (!empleado) return <div className="p-6 text-sm">El empleado no existe en la empresa activa. <Link to="/admin/empleados" className="underline">Volver a Empleados</Link></div>;

  const sucursalBase = sucursales.data?.items.find((s) => s.id === empleado.sucursalId);
  const departamento = departamentos.data?.items.find((d) => d.id === empleado.departamentoId);
  const puesto = puestos.data?.items.find((p) => p.id === empleado.puestoId);
  const asignadas = asignaciones.data?.items ?? [];
  const pendientes = sucursales.data?.items.filter((s) => s.estatus === EstatusCatalogo.Activo &&
    !asignadas.some((a) => a.sucursalId === s.id && a.estatus === EstatusCatalogo.Activo)) ?? [];
  const mutando = asignar.isPending || desactivar.isPending || reactivar.isPending;

  return <div className="mx-auto max-w-5xl space-y-4 p-4">
    <header className="flex flex-wrap items-center gap-3 border-b pb-3">
      <div className="min-w-0 flex-1">
        <h1 className="truncate text-lg font-semibold">{empleado.nombre}</h1>
        <p className="text-sm text-muted-foreground">{empleado.clave} · {empleado.email ?? 'Sin correo corporativo'}</p>
      </div>
      <Badge variant={empleado.estatus === EstatusCatalogo.Activo ? 'secondary' : 'outline'}>
        {empleado.estatus === EstatusCatalogo.Activo ? 'Activo' : 'Inactivo'}
      </Badge>
      <Button asChild size="sm" variant="outline"><Link to="/admin/empleados">Volver</Link></Button>
    </header>

    <nav role="tablist" aria-label="Secciones del empleado" className="flex flex-wrap gap-2 border-b pb-2">
      {([['datos', 'Datos generales'], ['puesto', 'Puesto y departamento'], ['sucursales', 'Sucursales asignadas'], ['acceso', 'Roles y accesos']] as const).map(([key, label]) =>
        <Button key={key} type="button" size="sm" variant={tab === key ? 'default' : 'ghost'}
          role="tab" aria-selected={tab === key} onClick={() => setTab(key)}>{label}</Button>)}
    </nav>

    {tab === 'datos' && <section role="tabpanel" className="space-y-3">
      <p className="text-sm">Sucursal base: {sucursalBase?.nombre ?? 'Sin asignar'}</p>
      <p className="text-sm">Correo: {empleado.email ?? 'Sin registrar'}</p>
      {editando ? <EmpleadoInlineForm empleado={empleado} onCancel={() => setEditando(false)} onSaved={() => setEditando(false)} />
        : <Button size="sm" variant="outline" onClick={() => setEditando(true)}>Editar datos y transferir sucursal base</Button>}
    </section>}

    {tab === 'puesto' && <section role="tabpanel" className="space-y-2 text-sm">
      <p>Departamento: {departamento?.nombre ?? 'Sin asignar'}</p>
      <p>Puesto: {puesto?.nombre ?? 'Sin asignar'}</p>
      <p>Rol sugerido por el puesto: {puesto?.rolSugeridoNombre ?? 'Sin sugerencia'}. La asignación vigente se administra en Roles y accesos.</p>
      <p className="text-muted-foreground">El rol define qué puede hacer; las sucursales asignadas definen dónde puede operar.</p>
    </section>}

    {tab === 'sucursales' && <section role="tabpanel" className="space-y-3">
      <p className="text-sm text-muted-foreground">La sucursal base es un dato laboral. Las sucursales operativas son permisos de alcance independientes.</p>
      {empleado.usuarioId == null ? <p className="text-sm">Este empleado aún no tiene cuenta de acceso; primero asígnala en Roles y accesos.</p>
        : !puedeGestionarSucursales ? <p className="text-sm">No tienes permiso para consultar o gestionar las asignaciones operativas de esta cuenta.</p>
        : asignaciones.isLoading ? <TableSkeleton rows={3} />
        : asignaciones.isError ? <ErrorState title="No se pudieron consultar las asignaciones" onRetry={() => asignaciones.refetch()} />
        : <>
          {asignadas.length === 0 ? <p className="text-sm">Sin sucursales operativas asignadas.</p> :
            <ul className="divide-y rounded-md border">{asignadas.map((a) => {
              const sucursal = sucursales.data?.items.find((s) => s.id === a.sucursalId);
              const activa = a.estatus === EstatusCatalogo.Activo;
              return <li key={a.sucursalId} className="flex flex-wrap items-center gap-2 p-3 text-sm">
                <span className="flex-1">{sucursal?.nombre ?? a.sucursalId}{a.sucursalId === empleado.sucursalId ? ' · base laboral' : ''}</span>
                <Badge variant={activa ? 'secondary' : 'outline'}>{activa ? 'Operativa' : 'Desactivada'}</Badge>
                {puedeGestionarSucursales && <Button size="sm" variant="outline" disabled={mutando || (!activa && !sucursal)}
                  onClick={() => {
                    const accion = activa ? desactivar : reactivar;
                    accion.mutate({ sucursalId: a.sucursalId, usuarioId: empleado.usuarioId!, idempotencyKey: crypto.randomUUID() },
                      { onSuccess: () => toast.success(activa ? 'Asignación desactivada; histórico conservado.' : 'Asignación reactivada.'), onError: error });
                  }}>{activa ? 'Desactivar' : 'Reactivar'}</Button>}
              </li>;
            })}</ul>}
          {puedeGestionarSucursales && pendientes.length > 0 && <div className="flex flex-wrap gap-2">
            <select aria-label="Sucursal operativa para asignar" className="h-9 rounded-md border bg-background px-2 text-sm"
              value={nuevaSucursal} onChange={(e) => setNuevaSucursal(e.target.value)}>
              <option value="">Selecciona una sucursal</option>
              {pendientes.map((s) => <option key={s.id} value={s.id}>{s.nombre}</option>)}
            </select>
            <Button size="sm" disabled={!nuevaSucursal || mutando} onClick={() => {
              const previa = asignadas.find((a) => a.sucursalId === nuevaSucursal);
              const accion = previa ? reactivar : asignar;
              accion.mutate({ sucursalId: nuevaSucursal, usuarioId: empleado.usuarioId!, idempotencyKey: crypto.randomUUID() },
                { onSuccess: () => { setNuevaSucursal(''); toast.success('Sucursal operativa asignada.'); }, onError: error });
            }}>Asignar sucursal</Button>
          </div>}
        </>}
    </section>}

    {tab === 'acceso' && <section role="tabpanel" className="space-y-4">
      <EmpleadoAccesoPanel empleadoId={empleado.id} usuarioId={empleado.usuarioId}
        email={empleado.email} empleadoActivo={empleado.estatus === EstatusCatalogo.Activo} />
      {empleado.usuarioId && puedeLeerUsuarios && <>
        {usuario.isLoading ? <TableSkeleton rows={3} /> : usuario.isError ?
          <ErrorState title="No se pudieron consultar los roles" onRetry={() => usuario.refetch()} /> :
          <RolesPorEmpresaPanel usuarioId={empleado.usuarioId} asignaciones={usuario.data?.asignaciones ?? []} />}
        <Button asChild variant="link" size="sm"><Link to="/admin/usuarios/$id" params={{ id: empleado.usuarioId }}>Abrir cuenta de acceso</Link></Button>
      </>}
      {empleado.usuarioId && !puedeLeerUsuarios && <p className="text-sm text-muted-foreground">No tienes permiso para consultar el detalle de roles de esta cuenta.</p>}
    </section>}
  </div>;
}
