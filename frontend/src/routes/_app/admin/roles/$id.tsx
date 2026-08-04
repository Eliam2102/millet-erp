import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { RolesLayout } from '@/modules/identidad/components/RolesLayout';
import { RolDetalle } from '@/modules/identidad/components/RolDetalle';
import { NuevoRolProvider } from '@/modules/identidad/components/SheetNuevoRol';

/**
 * <c>/admin/roles/$id</c> — detalle de rol dentro del layout
 * master-detail. La lista compacta queda a la izquierda con el id
 * activo highlighteado; el detalle (datos + permisos + grupos) ocupa
 * el panel derecho.
 *
 * <para>Guard: <c>identidad.roles.leer</c>. Si el id no existe o el
 * usuario no tiene permiso por row, el detalle renderiza
 * <c>ErrorState</c> sin tirar la ruta — la lista sigue navegable.</para>
 */
export const Route = createFileRoute('/_app/admin/roles/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.IdentidadRolesLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/admin/roles/$id' });
  // <c>key={id}</c>: fuerza re-mount al navegar A→B entre dos roles.
  // Mismo razonamiento que <c>/admin/empresas/$id</c>.
  return (
    <NuevoRolProvider>
      <RolesLayout idActivo={id} detalle={<RolDetalle key={id} />} />
    </NuevoRolProvider>
  );
}
