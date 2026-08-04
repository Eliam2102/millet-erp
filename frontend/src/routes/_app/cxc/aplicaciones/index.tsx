import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaAplicaciones } from '@/features/cxc/pages/BandejaAplicaciones';
import {
  AplicacionesSearchSchema,
  type AplicacionesSearch,
} from '@/features/cxc/lib/aplicaciones-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de propuestas de aplicación — <c>/cxc/aplicaciones</c>
 * (CXC-FE-PR6, P1). El backend gatea lectura/propuesta con
 * <c>aplicacion-pago.proponer</c>; confirmar/rechazar (Ingresos) con
 * <c>aplicacion-pago.confirmar</c>.
 */
export const Route = createFileRoute('/_app/cxc/aplicaciones/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer,
      PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: BandejaAplicaciones,
  validateSearch: (input: Record<string, unknown>): AplicacionesSearch =>
    AplicacionesSearchSchema.parse(input),
});
