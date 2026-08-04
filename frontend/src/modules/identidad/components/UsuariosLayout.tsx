import { useMemo, type ReactNode } from 'react';
import { Plus, Users } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useUsuarios } from '@/modules/identidad/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaUsuariosCompacta } from '@/modules/identidad/components/ListaUsuariosCompacta';
import { useNuevoUsuario } from '@/modules/identidad/components/nuevo-usuario-context';
import { cn } from '@/lib/utils';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/usuarios</c>.
 *
 * <list>
 *   <item><b>Master</b> (320px sticky a la izquierda) — lista compacta
 *   de usuarios con highlight del id activo. Botón "Nuevo usuario" si
 *   <c>identidad.usuarios.crear</c>.</item>
 *   <item><b>Panel detalle</b> (resto del ancho) — recibe via
 *   <c>detalle</c> prop el contenido para el id seleccionado, o un
 *   placeholder cuando <c>idActivo</c> es <c>null</c>.</item>
 * </list>
 *
 * <para>La ruta <c>/admin/usuarios</c> renderiza con
 * <c>idActivo=null</c>; <c>/admin/usuarios/$id</c> con <c>idActivo</c>
 * = id de la URL y pasa <c>&lt;UsuarioDetalle/&gt;</c> como
 * <c>detalle</c>.</para>
 */
export interface UsuariosLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function UsuariosLayout({ idActivo, detalle }: UsuariosLayoutProps) {
  const nuevoUsuario = useNuevoUsuario();
  const canCrear = useHasPermission(PermisosCanonicos.IdentidadUsuariosCrear);
  const usuariosQuery = useUsuarios({ limit: 200 });

  const items = useMemo(
    () => usuariosQuery.data?.items ?? [],
    [usuariosQuery.data],
  );

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de usuarios"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">Usuarios</h1>
          {canCrear && (
            <Button size="sm" onClick={() => nuevoUsuario.abrir()}>
              <Plus className="mr-1 h-4 w-4" />
              Nuevo
            </Button>
          )}
        </div>

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderLista
            query={usuariosQuery}
            items={items}
            idActivo={idActivo}
            canCrear={canCrear}
            onAbrirNuevo={() => nuevoUsuario.abrir()}
          />
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de usuario"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface RenderListaProps {
  query: ReturnType<typeof useUsuarios>;
  items: ReturnType<typeof useUsuarios>['data'] extends
    | { items: infer I }
    | undefined
    ? I
    : never;
  idActivo: string | null;
  canCrear: boolean;
  onAbrirNuevo: () => void;
}

function RenderLista({
  query,
  items,
  idActivo,
  canCrear,
  onAbrirNuevo,
}: RenderListaProps) {
  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton
          rows={6}
          columns={[{ width: 'w-full' }, { width: 'w-full' }]}
        />
      </div>
    );
  }

  if (query.isError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => query.refetch()} />;
  }

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Users className="h-10 w-10" />}
        title="Aún no hay usuarios."
        description={
          canCrear
            ? 'Crea el primero para empezar.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        action={
          canCrear ? (
            <Button size="sm" onClick={onAbrirNuevo}>
              <Plus className="mr-1 h-4 w-4" />
              Nuevo usuario
            </Button>
          ) : undefined
        }
      />
    );
  }

  return <ListaUsuariosCompacta items={items} idActivo={idActivo} />;
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Users className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona un usuario de la lista</p>
        <p className="text-xs">para ver sus datos y asignaciones.</p>
      </div>
    </div>
  );
}
