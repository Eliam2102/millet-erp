import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { RolesLayout } from '@/modules/identidad/components/RolesLayout';
import { NuevoRolProvider } from '@/modules/identidad/components/SheetNuevoRol';

/**
 * <c>/admin/roles</c> — bandeja master-detail de roles (UF-Admin-PR3).
 * Sin <c>$id</c> en la URL, el panel detalle muestra el placeholder
 * "Selecciona un rol…".
 *
 * <para><b>Guard</b>: requiere <c>identidad.roles.leer</c>. Sin
 * permiso, redirige a <c>/</c> — el gear del Topbar ya hace el mismo
 * gate; la URL directa también debe estar protegida.</para>
 *
 * <para>El <c>&lt;NuevoRolProvider/&gt;</c> envuelve la página porque
 * el Sheet "Nuevo rol" solo aparece dentro del módulo Identidad — no
 * es un recurso transversal del shell.</para>
 */
export const Route = createFileRoute('/_app/admin/roles/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.IdentidadRolesLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: RolesIndexRoute,
});

function RolesIndexRoute() {
  return (
    <NuevoRolProvider>
      <RolesLayout idActivo={null} />
    </NuevoRolProvider>
  );
}
