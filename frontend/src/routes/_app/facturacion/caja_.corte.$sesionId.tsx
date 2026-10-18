import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteCorteCaja } from '@/features/facturacion/pages/ReporteCorteCaja';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Corte de caja de una sesión —
 * <c>/facturacion/caja/corte/$sesionId</c> (FIX-12). Cuelga de «Mi caja»
 * para heredar el panel del módulo, el breadcrumb y el permiso de su card. El cajero
 * (caja.operar) ve el de su sesión; supervisar, administrar o liquidar ven
 * cualquiera. El backend decide el acceso por sesión (403 si no aplica).
 */
export const Route = createFileRoute('/_app/facturacion/caja_/corte/$sesionId')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.FacturacionCajaOperar,
      PermisosCanonicos.FacturacionCajaSupervisar,
      PermisosCanonicos.FacturacionCajaAdministrar,
      PermisosCanonicos.FacturacionCajaLiquidar,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: ReporteCorteCaja,
});
