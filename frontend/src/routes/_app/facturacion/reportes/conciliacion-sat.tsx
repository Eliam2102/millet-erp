import { createFileRoute, redirect } from '@tanstack/react-router';
import { ConciliacionSat } from '@/features/facturacion/pages/ConciliacionSat';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Conciliación de CFDI emitidos contra el SAT — <c>/facturacion/reportes/conciliacion-sat</c>
 * (FAC-10). Es de toda la empresa: la ven los perfiles corporativos de reportes.
 */
export const Route = createFileRoute('/_app/facturacion/reportes/conciliacion-sat')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionReportesLeerTodasSucursales)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: ConciliacionSat,
});
