import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpresasLayout } from '@/modules/administracion/components/EmpresasLayout';
import { EmpresaDetalle } from '@/modules/administracion/components/EmpresaDetalle';
import { NuevaEmpresaProvider } from '@/modules/administracion/components/SheetNuevaEmpresa';

/**
 * <c>/admin/empresas/$id</c> — detalle de empresa dentro del layout
 * master-detail. La lista compacta queda a la izquierda con el id
 * activo highlighteado; el detalle (datos + sucursales +
 * departamentos) ocupa el panel derecho.
 *
 * <para>Guard: <c>admin.empresas.leer</c>. Si el id no existe o el
 * usuario no tiene permiso por row, el detalle renderiza
 * <c>ErrorState</c> sin tirar la ruta — la lista sigue navegable.</para>
 */
export const Route = createFileRoute('/_app/admin/empresas/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminEmpresasLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/admin/empresas/$id' });
  // <c>key={id}</c>: fuerza re-mount del detalle al navegar entre
  // dos empresas (A→B). React reusaría la instancia (mismo tipo,
  // misma posición) y, aunque <c>useParams</c> sí reacciona, hooks
  // y estado local (tab activa, dialogs de confirm) podrían quedar
  // stale en el cruce. El re-mount es barato y elimina la clase
  // entera de bugs de "detalle no se actualiza".
  return (
    <NuevaEmpresaProvider>
      <EmpresasLayout idActivo={id} detalle={<EmpresaDetalle key={id} />} />
    </NuevaEmpresaProvider>
  );
}
