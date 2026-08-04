import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ClientesLayout } from '@/modules/datos-maestros/components/ClientesLayout';
import { NuevoClienteProvider } from '@/modules/datos-maestros/components/SheetNuevoCliente';

/**
 * <c>/admin/datos-maestros/clientes</c> — bandeja master-detail de
 * clientes (ADR-0048). Sin <c>$id</c> en la URL, el panel detalle
 * muestra el placeholder.
 *
 * <para><b>Guard</b>: requiere
 * <c>datos_maestros.clientes.gestionar</c>. Sin permiso, redirige
 * a <c>/</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/datos-maestros/clientes/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.DatosMaestrosClientesGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: ClientesIndexRoute,
});

function ClientesIndexRoute() {
  return (
    <NuevoClienteProvider>
      <ClientesLayout idActivo={null} />
    </NuevoClienteProvider>
  );
}
