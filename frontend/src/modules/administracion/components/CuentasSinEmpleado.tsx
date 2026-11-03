import { useState } from 'react';
import { Link2, Search, UserPlus, UserRound, X } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError } from '@/lib/api';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import {
  useCuentasSinEmpleado,
  useDarAccesoColaborador,
  useEmpleadosAdmin,
  type CuentaSinEmpleado,
} from '@/modules/administracion/api/empleados';
import { useRoles } from '@/modules/identidad/api/roles';

interface Props {
  /** Abre el alta de empleado con nombre y correo de la cuenta y acceso «Ya tiene cuenta Microsoft». */
  onCrearEmpleado: (cuenta: CuentaSinEmpleado) => void;
}

/**
 * Vista «Cuentas sin empleado» de Empleados (86e3x6cpw): cuentas Microsoft del
 * directorio, o usuarios del ERP, que aún no tienen ficha de empleado. Desde
 * aquí se crea el empleado con la cuenta o se liga a un empleado existente;
 * ya no hay cuentas de acceso independientes.
 */
export function CuentasSinEmpleado({ onCrearEmpleado }: Props) {
  const [busqueda, setBusqueda] = useState('');
  const buscar = useDebouncedValue(busqueda, 200);
  const [ligandoId, setLigandoId] = useState<string | null>(null);
  const query = useCuentasSinEmpleado(buscar);
  const items = query.data?.items ?? [];

  return (
    <div className="space-y-3">
      <div className="relative max-w-md">
        <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-ink-muted" />
        <Input
          aria-label="Buscar cuenta"
          placeholder="Buscar por nombre o correo..."
          value={busqueda}
          onChange={(e) => setBusqueda(e.target.value)}
          className="h-9 pl-9 pr-8 text-sm"
        />
        {busqueda && (
          <button
            type="button"
            aria-label="Limpiar búsqueda de cuentas"
            onClick={() => setBusqueda('')}
            className="absolute right-2.5 top-2.5 text-ink-muted hover:text-ink"
          >
            <X className="h-4 w-4" />
          </button>
        )}
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron consultar las cuentas sin empleado"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={4} />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<UserRound className="h-10 w-10" />}
          title={
            buscar.trim()
              ? 'Ninguna cuenta coincide con la búsqueda'
              : 'Todas las cuentas tienen empleado'
          }
          description={
            buscar.trim()
              ? 'Prueba con otro nombre o correo.'
              : 'Cada cuenta Microsoft con acceso ya está ligada a una ficha de empleado.'
          }
        />
      ) : (
        <ul
          aria-label="Cuentas sin empleado"
          className="divide-y rounded-lg border bg-surface-card shadow-card-flat"
        >
          {items.map((c) => (
            <li key={c.objectId} className="space-y-3 p-3 text-sm">
              <div className="flex flex-wrap items-center gap-2">
                <div className="min-w-0 flex-1">
                  <p className="font-medium text-ink">{c.nombre}</p>
                  <p className="truncate font-mono text-xs text-ink-muted">{c.correo}</p>
                </div>
                <Badge variant={c.yaEntroAlErp ? 'success' : 'neutral'}>
                  {c.yaEntroAlErp ? 'Ya entró al ERP' : 'Sin entrar'}
                </Badge>
                {!c.habilitada && <Badge variant="danger">Deshabilitada</Badge>}
                {c.habilitada && (
                  <div className="flex flex-wrap gap-2">
                    <Button size="sm" variant="outline" onClick={() => onCrearEmpleado(c)}>
                      <UserPlus className="mr-1.5 h-4 w-4" />
                      Crear empleado con esta cuenta
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      aria-expanded={ligandoId === c.objectId}
                      onClick={() => setLigandoId(ligandoId === c.objectId ? null : c.objectId)}
                    >
                      <Link2 className="mr-1.5 h-4 w-4" />
                      Ligar a empleado existente
                    </Button>
                  </div>
                )}
              </div>
              {ligandoId === c.objectId && c.habilitada && (
                <LigarCuentaForm cuenta={c} onCerrar={() => setLigandoId(null)} />
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Liga la cuenta a un empleado activo sin acceso (POST /colaboradores/{id}/acceso, cuenta existente). */
function LigarCuentaForm({
  cuenta,
  onCerrar,
}: {
  cuenta: CuentaSinEmpleado;
  onCerrar: () => void;
}) {
  const empleados = useEmpleadosAdmin();
  const roles = useRoles({ soloActivos: true, limit: 200 });
  const dar = useDarAccesoColaborador();
  const [empleadoId, setEmpleadoId] = useState('');
  const [rolId, setRolId] = useState('');
  const candidatos = (empleados.data ?? []).filter(
    (e) => e.estatus === EstatusCatalogo.Activo && e.usuarioId == null,
  );
  const empleado = candidatos.find((e) => e.id === empleadoId);

  function ligar() {
    if (!empleado || !rolId) {
      toast.error('Elige el empleado y el rol.');
      return;
    }
    dar.mutate(
      {
        empleadoId: empleado.id,
        acceso: 1,
        correoCorporativo: cuenta.correo,
        emailContacto: null,
        rolId,
        departamentoId: empleado.departamentoId,
        puestoId: empleado.puestoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`Cuenta ${cuenta.correo} ligada a ${empleado.nombre}.`);
          onCerrar();
        },
        onError: (err) => {
          if (esApiError(err) && err.code === 'COLABORADOR_ESTRUCTURA_INCOMPLETA') {
            toast.error('Asigna departamento y puesto al empleado antes de ligarle la cuenta.');
            return;
          }
          toast.error(esApiError(err) ? err.problem.title : 'No se pudo ligar la cuenta.');
        },
      },
    );
  }

  return (
    <div className="flex flex-wrap items-end gap-2 rounded-md border border-dashed border-brand p-3">
      <label className="text-xs">
        Empleado sin cuenta
        <select
          aria-label="Empleado sin cuenta"
          className="mt-1 block h-9 min-w-60 rounded-md border bg-surface-page px-2 text-sm"
          value={empleadoId}
          onChange={(e) => setEmpleadoId(e.target.value)}
          disabled={empleados.isLoading}
        >
          <option value="">
            {candidatos.length === 0 && !empleados.isLoading
              ? 'No hay empleados activos sin cuenta'
              : 'Selecciona un empleado'}
          </option>
          {candidatos.map((e) => (
            <option key={e.id} value={e.id}>
              {e.clave} · {e.nombre}
            </option>
          ))}
        </select>
      </label>
      <label className="text-xs">
        Rol en la empresa
        <select
          aria-label="Rol en la empresa"
          className="mt-1 block h-9 min-w-48 rounded-md border bg-surface-page px-2 text-sm"
          value={rolId}
          onChange={(e) => setRolId(e.target.value)}
        >
          <option value="">Selecciona un rol</option>
          {(roles.data?.items ?? [])
            .filter((r) => r.activo)
            .map((r) => (
              <option key={r.id} value={r.id}>
                {r.nombre}
              </option>
            ))}
        </select>
      </label>
      <Button size="sm" disabled={!empleadoId || !rolId || dar.isPending} onClick={ligar}>
        {dar.isPending ? 'Ligando…' : 'Ligar cuenta'}
      </Button>
      <Button size="sm" variant="ghost" onClick={onCerrar}>
        Cancelar
      </Button>
    </div>
  );
}
