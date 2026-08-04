import { createFileRoute, redirect } from '@tanstack/react-router';
import { MiCaja } from '@/features/facturacion/pages/MiCaja';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Panel "Mi caja" — <c>/facturacion/caja</c> (CAJAS-PR6, 12-cajas.md §5).
 * Operar abre sesión y cobra; supervisar autoriza aperturas ajenas y
 * reabre; liquidar cierra el arqueo.
 */
export const Route = createFileRoute('/_app/facturacion/caja')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const alguno = [
      PermisosCanonicos.FacturacionCajaOperar,
      PermisosCanonicos.FacturacionCajaSupervisar,
      PermisosCanonicos.FacturacionCajaLiquidar,
    ].some((p) => permisos.includes(p));
    if (!alguno) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: MiCaja,
});
