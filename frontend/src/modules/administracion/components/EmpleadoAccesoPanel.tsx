import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import {
  useAccesoColaborador,
  useAccionAccesoColaborador,
  useDarAccesoColaborador,
} from '@/modules/administracion/api/empleados';
import { useRoles } from '@/modules/identidad/api/roles';
import { useReactivarUsuario } from '@/modules/identidad/api/usuarios';

interface Props {
  empleadoId: string;
  usuarioId: string | null;
  email: string | null;
  empleadoActivo: boolean;
}

const ESTADOS = ['Acceso activo', 'Pendiente de primer acceso', 'Creando cuenta Microsoft', 'Error de provisión'];

export function EmpleadoAccesoPanel({ empleadoId, usuarioId, email, empleadoActivo }: Props) {
  const queryClient = useQueryClient();
  const puedeCrear = useHasPermission(PermisosCanonicos.IdentidadUsuariosCrear);
  const puedeAsignar = useHasPermission(PermisosCanonicos.IdentidadAsignacionesAdministrar);
  const puedeEditar = useHasPermission(PermisosCanonicos.IdentidadUsuariosEditar);
  const [tipo, setTipo] = useState<1 | 2>(1);
  const [correo, setCorreo] = useState(email ?? '');
  const [contacto, setContacto] = useState('');
  const [rolId, setRolId] = useState('');
  const roles = useRoles({ soloActivos: true, limit: 200 }, usuarioId == null && puedeCrear && puedeAsignar);
  const acceso = useAccesoColaborador(usuarioId ? empleadoId : null);
  const dar = useDarAccesoColaborador();
  const reintentar = useAccionAccesoColaborador('reintentar');
  const reenviar = useAccionAccesoColaborador('reenviar');
  const reactivar = useReactivarUsuario();

  function error(err: Error) {
    toast.error(esApiError(err) ? err.problem.title : 'No se pudo actualizar el acceso.');
  }

  if (!empleadoActivo) {
    return <p className="text-sm text-muted-foreground">Reactiva al empleado antes de gestionar su acceso. Su usuario permanece bloqueado.</p>;
  }

  if (usuarioId == null) {
    if (!puedeCrear || !puedeAsignar) {
      return <p className="text-sm text-muted-foreground">Sin acceso al ERP. No tienes permisos para crearlo.</p>;
    }
    return (
      <form className="grid gap-2 rounded-md border p-3 md:grid-cols-2" onSubmit={(event) => {
        event.preventDefault();
        if (!rolId || !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(correo.trim())) {
          toast.error('Indica correo corporativo y rol válidos.');
          return;
        }
        if (tipo === 2 && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(contacto.trim())) {
          toast.error('Indica el correo personal para enviar la contraseña temporal.');
          return;
        }
        dar.mutate({ empleadoId, acceso: tipo, correoCorporativo: correo.trim(),
          emailContacto: tipo === 2 ? contacto.trim() : null, rolId,
          idempotencyKey: crypto.randomUUID() }, {
          onSuccess: () => toast.success(tipo === 2
            ? 'Cuenta en provisión. Revisa su estado antes de confirmar el envío.'
            : 'Cuenta Microsoft vinculada al colaborador.'),
          onError: error,
        });
      }}>
        <div className="md:col-span-2 text-sm font-medium">Dar acceso al ERP</div>
        <label className="text-xs">Modo de acceso
          <select className="mt-1 h-9 w-full rounded-md border bg-background px-2 text-sm"
            value={tipo} onChange={(e) => setTipo(Number(e.target.value) as 1 | 2)}>
            <option value={1}>Ya tiene cuenta Microsoft</option>
            <option value={2}>Crear cuenta Microsoft</option>
          </select>
        </label>
        <label className="text-xs">Correo corporativo
          <Input className="mt-1" type="email" value={correo} onChange={(e) => setCorreo(e.target.value)} required />
        </label>
        <label className="text-xs">Rol en la empresa
          <select className="mt-1 h-9 w-full rounded-md border bg-background px-2 text-sm"
            value={rolId} onChange={(e) => setRolId(e.target.value)} required>
            <option value="">Selecciona un rol</option>
            {(roles.data?.items ?? []).filter((r) => r.activo).map((r) =>
              <option key={r.id} value={r.id}>{r.nombre}</option>)}
          </select>
        </label>
        {tipo === 2 && <label className="text-xs">Correo personal de contacto
          <Input className="mt-1" type="email" value={contacto} onChange={(e) => setContacto(e.target.value)} required />
        </label>}
        <div className="md:col-span-2">
          <Button type="submit" size="sm" disabled={dar.isPending}>Dar acceso</Button>
        </div>
      </form>
    );
  }

  if (acceso.isLoading) return <p className="text-sm text-muted-foreground">Cargando acceso…</p>;
  if (acceso.isError || !acceso.data) return <p className="text-sm text-destructive">No se pudo consultar el acceso.</p>;
  const estado = acceso.data;
  const pendiente = reintentar.isPending || reenviar.isPending || reactivar.isPending;
  return (
    <div className="space-y-2 rounded-md border p-3 text-sm">
      <p><span className="font-medium">{ESTADOS[estado.estadoAcceso]}</span> · {estado.email}</p>
      {!estado.usuarioActivo && <p className="text-amber-700">Usuario bloqueado: no puede entrar al ERP.</p>}
      {estado.motivoErrorProvision && <p className="text-destructive">{estado.motivoErrorProvision}</p>}
      {estado.emailContacto && <p className="text-muted-foreground">Correo de contacto: {estado.emailContacto}</p>}
      {estado.accesoEnviadoEn && <p className="text-muted-foreground">Solicitud de envío aceptada: {new Date(estado.accesoEnviadoEn).toLocaleString()}</p>}
      <div className="flex flex-wrap gap-2">
        {!estado.usuarioActivo && puedeEditar && <Button size="sm" variant="outline" disabled={pendiente}
          onClick={() => reactivar.mutate({ id: usuarioId, idempotencyKey: crypto.randomUUID() },
            {
              onSuccess: () => {
                acceso.refetch();
                queryClient.invalidateQueries({ queryKey: adminKeys.empleados() });
                queryClient.invalidateQueries({ queryKey: ['catalogos', 'empleados'] });
                toast.success('Usuario reactivado');
              },
              onError: error,
            })}>
          Reactivar acceso
        </Button>}
        {estado.estadoAcceso === 3 && puedeCrear && <Button size="sm" variant="outline" disabled={pendiente}
          onClick={() => reintentar.mutate({ empleadoId, idempotencyKey: crypto.randomUUID() },
            { onSuccess: () => toast.success('Provisión reintentada'), onError: error })}>
          Reintentar creación
        </Button>}
        {estado.estadoAcceso === 1 && puedeCrear && estado.usuarioActivo && <Button size="sm" variant="outline" disabled={pendiente}
          onClick={() => reenviar.mutate({ empleadoId, idempotencyKey: crypto.randomUUID() },
            { onSuccess: () => toast.success('Solicitud de correo aceptada; la entrega queda por verificar.'), onError: error })}>
          Reenviar acceso
        </Button>}
      </div>
    </div>
  );
}
