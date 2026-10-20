import { DialogBody } from '@/components/ui/dialog';
import { ConfirmacionContent as AlertDialogContent } from '@/modules/administracion/components/ConfirmacionContent';
import { PageHeader } from '@/components/erp/PageHeader';
import { ESTADOS_CUENTA } from '../lib/estados-ui';
import { AlcanceUsuarioPanel } from '@/modules/administracion/components/AlcanceUsuarioPanel';
import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { PowerOff, Power, X } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
import { useDesactivarUsuario, useReactivarUsuario, useUsuario } from '@/modules/identidad/api';
import { esApiError, codigoSoporte } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useAuthStore } from '@/lib/auth/auth-store';
import { authMode } from '@/lib/auth/config';
import { useAuth } from '@/lib/auth/useAuth';
import { UsuarioDatosForm } from '@/modules/identidad/components/UsuarioDatosForm';
import { RolesPorEmpresaPanel } from '@/modules/identidad/components/RolesPorEmpresaPanel';
import { PreferenciasPanel } from '@/modules/identidad/components/PreferenciasPanel';
import { AsignacionUsuarioPanel } from '@/features/centros-costo/components/AsignacionUsuarioPanel';
import { cn } from '@/lib/utils';

type Tab = 'datos' | 'roles' | 'centros-costo' | 'preferencias';

/**
 * Detalle de usuario (P3 del patrón cross-módulo). Sub-topbar sticky
 * con identidad (email + nombre + badge Activo/Inactivo) + acción
 * Desactivar/Reactivar + botón cerrar. Tabs: Datos | Roles por
 * empresa | Centros de Costo | Preferencias.
 *
 * <para>El tab "Centros de Costo" (asignación de alcance Dim3) solo se
 * renderea con el permiso <c>centros_costo.asignaciones.administrar</c> —
 * el mismo que gatea la pantalla <c>/centros-costo/asignaciones</c> y su
 * endpoint (GET del árbol = 403 sin él). Sin permiso el tab no aparece.</para>
 *
 * <para>Lee <c>$id</c> del path con la forma estricta de
 * <c>useParams({ from: '/_app/admin/usuarios/$id' })</c> — el
 * componente solo se monta dentro de esa ruta, así que el tipo es
 * exacto y evita que <c>id</c> se cuele <c>undefined</c> en runtime.
 * 403/404 caen al <c>ErrorState</c>.</para>
 */
export function UsuarioDetalle() {
  const { id } = useParams({ from: '/_app/admin/usuarios/$id' });
  const usuarioQuery = useUsuario(id);
  const [tab, setTab] = useState<Tab>('datos');
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const canDesactivar = useHasPermission(PermisosCanonicos.IdentidadUsuariosDesactivar);
  const canEditar = useHasPermission(PermisosCanonicos.IdentidadUsuariosEditar);
  // Gate del tab "Centros de Costo": mismo permiso que la pantalla dedicada y
  // que el GET del árbol. Sin él, el tab no se renderea (no read-only, no
  // placeholder: el endpoint respondería 403 y no habría árbol que pintar).
  const canVerCentrosCosto = useHasPermission(
    PermisosCanonicos.CentrosCostoAsignacionesAdministrar,
  );
  const desactivar = useDesactivarUsuario();
  const reactivar = useReactivarUsuario();
  const currentUserId = useAuthStore((s) => s.user?.id);
  const { loginAsFakeUser, isLoading: cambiandoCuenta } = useAuth();
  const esUsuarioActual = currentUserId != null && currentUserId === usuarioQuery.data?.usuario.id;

  if (usuarioQuery.isError) {
    const problem = esApiError(usuarioQuery.error) ? usuarioQuery.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => usuarioQuery.refetch()} />;
  }

  if (usuarioQuery.isLoading || usuarioQuery.data == null) {
    return (
      <div className="space-y-5">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const { usuario, asignaciones } = usuarioQuery.data;

  function handleConfirmarDesactivar() {
    if (esUsuarioActual) {
      toast.error('No puedes desactivar tu propia cuenta activa.');
      setConfirmDesactivar(false);
      return;
    }

    desactivar.mutate(
      { id: usuario.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Usuario ${usuario.email} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
          } else {
            toast.error('Error al desactivar el usuario.');
          }
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      { id: usuario.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Usuario ${usuario.email} reactivado`);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
          } else {
            toast.error('Error al reactivar el usuario.');
          }
        },
      },
    );
  }

  return (
    <div className="flex flex-col gap-5">
      {/* Header NO sticky: viaja con el scroll. Sólo el <nav> queda
          pinneado para que los tabs estén siempre visibles sin que el
          bloque sticky tape la primera fila del form al anclar. */}
      <PageHeader
        mono
        titulo={usuario.email}
        descripcion={usuario.nombre}
        estado={
          <>
            {usuario.activo ? (
              <Badge variant={ESTADOS_CUENTA.activo.tono}>Activo</Badge>
            ) : (
              <Badge variant={ESTADOS_CUENTA.inactivo.tono}>Inactivo</Badge>
            )}{' '}
            <Badge variant={usuario.empleadoId ? 'success' : 'neutral'}>
              {usuario.empleadoId ? 'Empleado vinculado' : 'Sin empleado vinculado'}
            </Badge>
          </>
        }
        acciones={
          <div className="ml-auto flex items-center gap-1">
            {import.meta.env.DEV &&
              authMode === 'FakeForLocalDev' &&
              !usuario.entraOid.startsWith('pending:') &&
              usuario.activo && (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={cambiandoCuenta}
                  onClick={() =>
                    void loginAsFakeUser(usuario.entraOid, usuario.email, usuario.nombre)
                  }
                >
                  Simular ingreso
                </Button>
              )}
            {canDesactivar && usuario.activo && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                disabled={esUsuarioActual || desactivar.isPending}
                title={esUsuarioActual ? 'No puedes desactivar tu propia cuenta activa' : undefined}
                onClick={() => setConfirmDesactivar(true)}
              >
                <PowerOff className="mr-1.5 h-4 w-4" />
                Desactivar
              </Button>
            )}
            {canEditar && !usuario.activo && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={handleReactivar}
                disabled={reactivar.isPending}
              >
                <Power className="mr-1.5 h-4 w-4" />
                {reactivar.isPending ? 'Reactivando…' : 'Reactivar'}
              </Button>
            )}
            <Button asChild variant="ghost" size="icon">
              <Link to="/admin/usuarios" aria-label="Cerrar">
                <X className="h-4 w-4" />
              </Link>
            </Button>
          </div>
        }
      />
      <dl className="grid gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
        <div>
          <dt className="text-xs text-ink-muted">Correo</dt>
          <dd className="text-sm font-medium ">{usuario.email}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Nombre</dt>
          <dd className="text-sm font-medium ">{usuario.nombre}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Asignaciones</dt>
          <dd className="text-sm font-medium ">{asignaciones.length}</dd>
        </div>
      </dl>

      <nav
        className="sticky top-14 z-10 flex items-center gap-1 border-b bg-surface-page/95 px-4 "
        role="tablist"
        aria-label="Secciones del usuario"
        data-print="hidden"
      >
        <TabButton
          activa={tab === 'datos'}
          onClick={() => setTab('datos')}
          ariaControls="tab-panel-datos"
        >
          Datos
        </TabButton>
        <TabButton
          activa={tab === 'roles'}
          onClick={() => setTab('roles')}
          ariaControls="tab-panel-roles"
        >
          Roles por empresa{' '}
          <span className="ml-1 text-xs text-ink-muted">({asignaciones.length})</span>
        </TabButton>
        {canVerCentrosCosto && (
          <TabButton
            activa={tab === 'centros-costo'}
            onClick={() => setTab('centros-costo')}
            ariaControls="tab-panel-centros-costo"
          >
            Centros de Costo
          </TabButton>
        )}
        <TabButton
          activa={tab === 'preferencias'}
          onClick={() => setTab('preferencias')}
          ariaControls="tab-panel-preferencias"
        >
          Preferencias
        </TabButton>
      </nav>

      <div className="px-4 pt-4 pb-6">
        {tab === 'datos' && (
          <div id="tab-panel-datos" role="tabpanel">
            <UsuarioDatosForm usuario={usuario} />
            <AlcanceUsuarioPanel key={usuario.id} usuarioId={usuario.id} />
          </div>
        )}
        {tab === 'roles' && (
          <div id="tab-panel-roles" role="tabpanel">
            <RolesPorEmpresaPanel usuarioId={usuario.id} asignaciones={asignaciones} />
          </div>
        )}
        {tab === 'centros-costo' && canVerCentrosCosto && (
          <div id="tab-panel-centros-costo" role="tabpanel">
            <AsignacionUsuarioPanel usuarioId={usuario.id} />
          </div>
        )}
        {tab === 'preferencias' && (
          <div id="tab-panel-preferencias" role="tabpanel">
            <PreferenciasPanel usuarioId={usuario.id} />
          </div>
        )}
      </div>

      <AlertDialog open={confirmDesactivar} onOpenChange={setConfirmDesactivar}>
        <AlertDialogContent>
          <AlertDialogHeader className="px-6 pt-5 pb-3">
            <AlertDialogTitle>Desactivar usuario</AlertDialogTitle>
          </AlertDialogHeader>
          <DialogBody>
            <AlertDialogDescription>
              ¿Confirmas desactivar al usuario{' '}
              <span className="font-mono font-semibold">{usuario.email}</span>? Sus asignaciones se
              conservan en BD pero pierde acceso al ERP. La acción se revierte con "Reactivar".
            </AlertDialogDescription>
          </DialogBody>
          <AlertDialogFooter className="border-t border-line-divider bg-surface-subtle px-6 py-4">
            <AlertDialogCancel disabled={desactivar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarDesactivar}
              disabled={desactivar.isPending || esUsuarioActual}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

interface TabButtonProps {
  activa: boolean;
  onClick: () => void;
  ariaControls: string;
  children: React.ReactNode;
}

function TabButton({ activa, onClick, ariaControls, children }: TabButtonProps) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={activa}
      aria-controls={ariaControls}
      onClick={onClick}
      className={cn(
        'border-b-2 px-3 py-2 text-sm font-medium transition-colors',
        activa ? 'border-brand text-ink' : 'border-transparent text-ink-muted hover:text-ink',
      )}
    >
      {children}
    </button>
  );
}
