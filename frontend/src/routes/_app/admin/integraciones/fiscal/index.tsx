import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { IntegracionesFiscalPage } from '@/features/integraciones-fiscal/pages/IntegracionesFiscalPage';

/**
 * <c>/admin/integraciones/fiscal</c> — master-detail (Empresas a la
 * izquierda, Configuración del PAC + RFCs receptores a la derecha).
 *
 * <para><b>Guard</b>: requiere
 * <c>integraciones.fiscal.leer</c>. La mutación (form + agregar/quitar
 * RFC) está gateada adicionalmente por
 * <c>integraciones.fiscal.administrar</c> dentro de la página. Sin
 * permiso de lectura, redirige a <c>/</c> — la card del admin landing
 * ya hace el mismo gate; la URL directa también debe estar
 * protegida.</para>
 */
export const Route = createFileRoute('/_app/admin/integraciones/fiscal/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.IntegracionesFiscalLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: IntegracionesFiscalRoute,
});

function IntegracionesFiscalRoute() {
  return <IntegracionesFiscalPage />;
}
