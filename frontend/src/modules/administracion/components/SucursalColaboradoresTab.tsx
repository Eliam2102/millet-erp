import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Search, UserCheck, UserX, Users, ExternalLink, Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { useActualizarEmpleado, useEmpleadosAdmin } from '@/modules/administracion/api/empleados';
import { EmpleadoInlineForm } from '@/modules/administracion/components/EmpleadoInlineForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useDepartamentos, usePuestos } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';

export interface SucursalColaboradoresTabProps {
  sucursalId: string;
  canGestionar?: boolean;
}

export function SucursalColaboradoresTab({
  sucursalId,
  canGestionar = false,
}: SucursalColaboradoresTabProps) {
  const [filtro, setFiltro] = useState('');
  const [mostrarAlta, setMostrarAlta] = useState(false);
  const [empleadoId, setEmpleadoId] = useState('');
  const currentEmpresaId = useAuthStore((s) => s.currentEmpresaId);
  const actualizarEmpleado = useActualizarEmpleado();
  const empleadosQuery = useEmpleadosAdmin();
  const puestosQuery = usePuestos();
  const departamentosQuery = useDepartamentos();

  const puestosMap = useMemo(() => {
    const map = new Map<string, string>();
    puestosQuery.data?.items.forEach((p) => map.set(p.id, p.nombre));
    return map;
  }, [puestosQuery.data]);

  const deptosMap = useMemo(() => {
    const map = new Map<string, string>();
    departamentosQuery.data?.items.forEach((d) => map.set(d.id, d.nombre));
    return map;
  }, [departamentosQuery.data]);

  const empleadosSucursal = useMemo(() => {
    const items = empleadosQuery.data ?? [];
    return items.filter((e) => e.sucursalId === sucursalId);
  }, [empleadosQuery.data, sucursalId]);

  const colaboradoresSinSucursal = useMemo(() =>
    (empleadosQuery.data ?? []).filter((e) =>
      e.empresaId === currentEmpresaId && !e.sucursalId && e.estatus === EstatusCatalogo.Activo,
    ), [empleadosQuery.data, currentEmpresaId]);

  function vincularColaborador() {
    if (!empleadoId) return;
    actualizarEmpleado.mutate(
      { id: empleadoId, payload: { sucursalId }, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Colaborador vinculado a la sucursal');
          setEmpleadoId('');
        },
        onError: () => toast.error('No se pudo vincular al colaborador.'),
      },
    );
  }

  const empleadosFiltrados = useMemo(() => {
    const q = filtro.trim().toLowerCase();
    if (!q) return empleadosSucursal;
    return empleadosSucursal.filter((e) => {
      const matchClave = e.clave.toLowerCase().includes(q);
      const matchNombre = e.nombre.toLowerCase().includes(q);
      const matchEmail = (e.email ?? '').toLowerCase().includes(q);
      const puestoNombre = e.puestoNombre ?? (e.puestoId ? puestosMap.get(e.puestoId) : '');
      const matchPuesto = (puestoNombre ?? '').toLowerCase().includes(q);
      return matchClave || matchNombre || matchEmail || matchPuesto;
    });
  }, [empleadosSucursal, filtro, puestosMap]);

  if (empleadosQuery.isError) {
    return (
      <SucursalTabErrorState
        error={empleadosQuery.error}
        onRetry={() => empleadosQuery.refetch()}
      />
    );
  }

  const isLoading = empleadosQuery.isLoading;
  if (isLoading) {
    return (
      <div className="space-y-2">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-14 w-full" />
      </div>
    );
  }

  const totalConAccesoERP = empleadosSucursal.filter((e) => e.usuarioId != null).length;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <Users className="h-4 w-4" aria-hidden="true" />
          <span>Total en sucursal: {empleadosSucursal.length}</span>
          <span aria-hidden="true">·</span>
          <span>Con acceso ERP: {totalConAccesoERP}</span>
        </div>

        {canGestionar && (
          <Button asChild variant="outline" size="sm">
            <Link to="/admin/empleados">
              <ExternalLink className="mr-1 h-3.5 w-3.5" />
              Administrar empleados
            </Link>
          </Button>
        )}
      </div>

      <div className="relative">
        <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input
          type="search"
          placeholder="Buscar por clave, nombre, puesto o correo..."
          className="pl-8 text-sm"
          value={filtro}
          onChange={(e) => setFiltro(e.target.value)}
          aria-label="Buscar colaboradores en esta sucursal"
        />
      </div>

      {canGestionar && (
        <div className="space-y-3 rounded-md border p-3">
          <div className="flex flex-wrap items-center gap-2">
            <select
              aria-label="Colaborador sin sucursal"
              className="h-9 min-w-52 rounded-md border bg-background px-2 text-sm"
              value={empleadoId}
              onChange={(e) => setEmpleadoId(e.target.value)}
            >
              <option value="">Seleccionar colaborador sin sucursal</option>
              {colaboradoresSinSucursal.map((empleado) => (
                <option key={empleado.id} value={empleado.id}>
                  {empleado.clave} · {empleado.nombre}
                </option>
              ))}
            </select>
            <Button type="button" size="sm" disabled={!empleadoId || actualizarEmpleado.isPending} onClick={vincularColaborador}>
              Vincular a esta sucursal
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={() => setMostrarAlta((actual) => !actual)}>
              <Plus className="mr-1 h-3.5 w-3.5" />
              Nuevo colaborador
            </Button>
          </div>
          {mostrarAlta && (
            <EmpleadoInlineForm
              sucursalIdInicial={sucursalId}
              onCancel={() => setMostrarAlta(false)}
              onSaved={() => setMostrarAlta(false)}
            />
          )}
        </div>
      )}

      {empleadosSucursal.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-8 text-center text-sm text-muted-foreground">
          <Users className="mx-auto mb-2 h-8 w-8 text-muted-foreground/50" />
          <p className="font-medium text-foreground">
            No hay colaboradores asignados a esta sucursal
          </p>
          <p className="mt-1 text-xs">
            Los colaboradores se asignan desde el catálogo de Empleados indicando esta sucursal en su perfil operativo.
          </p>
        </div>
      ) : empleadosFiltrados.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No se encontraron colaboradores que coincidan con &ldquo;{filtro}&rdquo;.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {empleadosFiltrados.map((emp) => {
            const puesto = emp.puestoNombre ?? (emp.puestoId ? puestosMap.get(emp.puestoId) : null);
            const depto = emp.departamentoNombre ?? (emp.departamentoId ? deptosMap.get(emp.departamentoId) : null);
            const activo = emp.estatus === EstatusCatalogo.Activo;
            const tieneUsuario = emp.usuarioId != null;

            return (
              <li key={emp.id} className="p-3">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="flex min-w-0 flex-1 flex-col gap-1">
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-xs font-semibold text-muted-foreground">
                        {emp.clave}
                      </span>
                      <span className="truncate text-sm font-semibold text-foreground">
                        {emp.nombre}
                      </span>
                      {activo ? (
                        <Badge variant="secondary" className="text-[10px]">
                          Activo
                        </Badge>
                      ) : (
                        <Badge variant="outline" className="text-[10px] text-muted-foreground">
                          Inactivo
                        </Badge>
                      )}
                    </div>

                    <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
                      {puesto ? (
                        <span>
                          Puesto: <span className="font-medium text-foreground">{puesto}</span>
                        </span>
                      ) : (
                        <span className="italic text-muted-foreground/60">Sin puesto</span>
                      )}
                      <span aria-hidden="true">·</span>
                      {depto ? (
                        <span>
                          Depto: <span className="font-medium text-foreground">{depto}</span>
                        </span>
                      ) : (
                        <span className="italic text-muted-foreground/60">Sin departamento</span>
                      )}
                      {emp.email && (
                        <>
                          <span aria-hidden="true">·</span>
                          <span className="font-mono">{emp.email}</span>
                        </>
                      )}
                    </div>
                  </div>

                  <div className="flex items-center gap-1.5 self-center">
                    {tieneUsuario ? (
                      <Badge
                        variant="outline"
                        className="border-emerald-300 bg-emerald-50 text-[11px] font-medium text-emerald-800 dark:border-emerald-700 dark:bg-emerald-950 dark:text-emerald-300"
                        title="Cuenta con usuario asignado en el sistema"
                      >
                        <UserCheck className="mr-1 h-3 w-3 text-emerald-600 dark:text-emerald-400" />
                        Acceso ERP
                      </Badge>
                    ) : (
                      <Badge
                        variant="outline"
                        className="text-[11px] text-muted-foreground"
                        title="No tiene cuenta de usuario vinculada"
                      >
                        <UserX className="mr-1 h-3 w-3 text-muted-foreground/60" />
                        Sin acceso ERP
                      </Badge>
                    )}
                  </div>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
