import { useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { Trash2, X } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
import { useEliminarRol, useRol } from '@/modules/identidad/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { RolDatosForm } from '@/modules/identidad/components/RolDatosForm';
import { MatrizPermisos } from '@/modules/identidad/components/MatrizPermisos';
import { GruposEntraIdPanel } from '@/modules/identidad/components/GruposEntraIdPanel';
import { cn } from '@/lib/utils';

type Tab = 'datos' | 'permisos' | 'grupos';

/**
 * Detalle de rol (P3 del patrón cross-módulo). Sub-topbar sticky con
 * identidad (código + nombre + badges Sistema/Inactivo) + acción
 * "Eliminar" (soft-delete, deshabilitada si <c>esDelSistema</c>) +
 * botón cerrar. Tabs: Datos | Permisos | Grupos Entra ID.
 *
 * <para>403/404 caen al <c>ErrorState</c>; tabs se renderizan siempre
 * a partir del detalle cargado.</para>
 */
export function RolDetalle() {
  // Forma estricta del <c>useParams</c>: el componente solo se monta
  // bajo <c>/admin/roles/$id</c>, así que <c>strict: false</c> +
  // <c>as { id: string }</c> era innecesariamente laxo y podría
  // devolver <c>{}</c> ⇒ <c>id</c> undefined ⇒ <c>useRol(undefined)</c>
  // con <c>enabled: false</c> ⇒ TableSkeleton atascado. Bug paralelo
  // al de Empresas (F-Admin-PR2.4).
  const { id } = useParams({ from: '/_app/admin/roles/$id' });
  const navigate = useNavigate();
  const rolQuery = useRol(id);
  const [tab, setTab] = useState<Tab>('datos');
  const [confirmEliminar, setConfirmEliminar] = useState(false);

  const canEliminar = useHasPermission(
    PermisosCanonicos.IdentidadRolesEliminar,
  );
  const canAsignarPermisos = useHasPermission(
    PermisosCanonicos.IdentidadRolesAsignarPermisos,
  );

  const idempotencyKey = useFormIdempotencyKey();
  const eliminar = useEliminarRol();

  if (rolQuery.isError) {
    const problem = esApiError(rolQuery.error)
      ? rolQuery.error.problem
      : undefined;
    return <ErrorState problem={problem} onRetry={() => rolQuery.refetch()} />;
  }

  if (rolQuery.isLoading || rolQuery.data == null) {
    return (
      <div className="space-y-4 p-4">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const { rol, permisoIds, gruposEntraId } = rolQuery.data;
  const puedeEliminar = canEliminar && !rol.esDelSistema && rol.activo;

  function handleConfirmarEliminar() {
    eliminar.mutate(
      { id: rol.id, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`Rol "${rol.codigo}" eliminado`);
          setConfirmEliminar(false);
          navigate({ to: '/admin/roles' });
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
          } else {
            toast.error('Error al eliminar el rol.');
          }
          setConfirmEliminar(false);
        },
      },
    );
  }

  const matrizDisabled = !canAsignarPermisos || rol.esDelSistema;
  const matrizDisabledHint = rol.esDelSistema
    ? 'Este rol es del sistema y sus permisos no se pueden modificar.'
    : !canAsignarPermisos
      ? 'No tienes permiso para modificar los permisos de los roles.'
      : undefined;

  return (
    <div className="flex flex-col">
      {/* Header NO sticky: viaja con el scroll. Sólo el <nav> queda
          pinneado para que los tabs estén siempre visibles sin que el
          bloque sticky tape la primera fila del form al anclar. */}
      <header
        className="flex flex-wrap items-center gap-3 border-b bg-background px-4 py-2"
        data-print="hidden"
      >
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate font-mono text-sm font-semibold">
            {rol.codigo}
          </span>
          {rol.esDelSistema && <Badge variant="outline">Sistema</Badge>}
          {!rol.activo && (
            <Badge variant="outline" className="text-muted-foreground">
              Inactivo
            </Badge>
          )}
          <span className="hidden truncate text-sm text-muted-foreground md:inline">
            · {rol.nombre}
          </span>
        </div>

        <div className="ml-auto flex items-center gap-1">
          {puedeEliminar && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setConfirmEliminar(true)}
            >
              <Trash2 className="mr-1.5 h-4 w-4" />
              Eliminar
            </Button>
          )}
          <Button asChild variant="ghost" size="icon">
            <Link to="/admin/roles" aria-label="Cerrar">
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </header>

      <nav
        className="sticky top-14 z-10 flex items-center gap-1 border-b bg-background/95 px-4 backdrop-blur"
        role="tablist"
        aria-label="Secciones del rol"
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
            activa={tab === 'permisos'}
            onClick={() => setTab('permisos')}
            ariaControls="tab-panel-permisos"
          >
            Permisos{' '}
            <span className="ml-1 text-xs text-muted-foreground">
              ({permisoIds.length})
            </span>
          </TabButton>
          <TabButton
            activa={tab === 'grupos'}
            onClick={() => setTab('grupos')}
            ariaControls="tab-panel-grupos"
          >
            Grupos Entra ID{' '}
            <span className="ml-1 text-xs text-muted-foreground">
              ({gruposEntraId.length})
            </span>
          </TabButton>
        </nav>

      <div className="px-4 pt-4 pb-6">
        {tab === 'datos' && (
          <div id="tab-panel-datos" role="tabpanel">
            <RolDatosForm rol={rol} />
          </div>
        )}
        {tab === 'permisos' && (
          <div id="tab-panel-permisos" role="tabpanel">
            <MatrizPermisos
              rolId={rol.id}
              permisoIdsIniciales={permisoIds}
              disabled={matrizDisabled}
              disabledHint={matrizDisabledHint}
            />
          </div>
        )}
        {tab === 'grupos' && (
          <div id="tab-panel-grupos" role="tabpanel">
            <GruposEntraIdPanel
              rolId={rol.id}
              grupos={gruposEntraId}
              bloqueado={rol.esDelSistema}
            />
          </div>
        )}
      </div>

      <AlertDialog
        open={confirmEliminar}
        onOpenChange={setConfirmEliminar}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Eliminar rol</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas eliminar el rol{' '}
              <span className="font-mono font-semibold">{rol.codigo}</span>?
              La acción es un <em>soft-delete</em>: el rol queda inactivo y
              los usuarios pierden el acceso, pero el registro se conserva
              para auditoría.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={eliminar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarEliminar}
              disabled={eliminar.isPending}
            >
              {eliminar.isPending ? 'Eliminando…' : 'Eliminar'}
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
        activa
          ? 'border-primary text-foreground'
          : 'border-transparent text-muted-foreground hover:text-foreground',
      )}
    >
      {children}
    </button>
  );
}
