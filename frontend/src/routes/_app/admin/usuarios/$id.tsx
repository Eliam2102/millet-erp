import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { UsuariosLayout } from '@/modules/identidad/components/UsuariosLayout';
import { UsuarioDetalle } from '@/modules/identidad/components/UsuarioDetalle';
import { NuevoUsuarioProvider } from '@/modules/identidad/components/SheetNuevoUsuario';

/**
 * <c>/admin/usuarios/$id</c> — detalle de usuario dentro del layout
 * master-detail. La lista compacta queda a la izquierda con el id
 * activo highlighteado; el detalle (datos + roles por empresa +
 * preferencias) ocupa el panel derecho.
 *
 * <para>Guard: <c>identidad.usuarios.leer</c>. Si el id no existe o
 * el usuario no tiene permiso por row, el detalle renderiza
 * <c>ErrorState</c> sin tirar la ruta — la lista sigue navegable.</para>
 */
export const Route = createFileRoute('/_app/admin/usuarios/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.IdentidadUsuariosLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/admin/usuarios/$id' });
  // <c>key={id}</c>: fuerza re-mount al navegar A→B entre dos
  // usuarios. Mismo razonamiento que <c>/admin/empresas/$id</c> /
  // <c>/admin/roles/$id</c>.
  return (
    <NuevoUsuarioProvider>
      <UsuariosLayout idActivo={id} detalle={<UsuarioDetalle key={id} />} />
    </NuevoUsuarioProvider>
  );
}
