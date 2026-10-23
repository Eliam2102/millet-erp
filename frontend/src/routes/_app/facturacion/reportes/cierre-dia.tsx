import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteCierreDia } from '@/features/facturacion/pages/ReporteCierreDia';
import {
  CierreDiaSearchSchema,
  type CierreDiaSearch,
} from '@/features/facturacion/lib/reportes-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Cierre del día por sucursal — <c>/facturacion/reportes/cierre-dia</c>
 * (F6.7): monitor de CFDI y facturado contra ingresado.
 */
export const Route = createFileRoute('/_app/facturacion/reportes/cierre-dia')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionReportesLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: ReporteCierreDia,
  validateSearch: (input: Record<string, unknown>): CierreDiaSearch =>
    CierreDiaSearchSchema.parse(input),
});
