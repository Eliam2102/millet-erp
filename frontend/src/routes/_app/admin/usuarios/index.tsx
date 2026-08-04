import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { UsuariosLayout } from '@/modules/identidad/components/UsuariosLayout';
import { NuevoUsuarioProvider } from '@/modules/identidad/components/SheetNuevoUsuario';

/**
 * <c>/admin/usuarios</c> — bandeja master-detail de usuarios
 * (UF-Admin-PR4). Sin <c>$id</c> en la URL, el panel detalle muestra el
 * placeholder "Selecciona un usuario…".
 *
 * <para><b>Guard</b>: requiere <c>identidad.usuarios.leer</c>. Sin
 * permiso, redirige a <c>/</c> — el gear del Topbar ya hace el mismo
 * gate; la URL directa también debe estar protegida.</para>
 *
 * <para>El <c>&lt;NuevoUsuarioProvider/&gt;</c> envuelve la página
 * porque el Sheet "Nuevo usuario" solo aparece dentro del módulo
 * Identidad — no es un recurso transversal del shell.</para>
 */
export const Route = createFileRoute('/_app/admin/usuarios/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.IdentidadUsuariosLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: UsuariosIndexRoute,
});

function UsuariosIndexRoute() {
  return (
    <NuevoUsuarioProvider>
      <UsuariosLayout idActivo={null} />
    </NuevoUsuarioProvider>
  );
}
