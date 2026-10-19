import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteComparativaAnticipos } from '@/features/facturacion/pages/ReporteComparativaAnticipos';
import {
  ComparativaAnticiposSearchSchema,
  type ComparativaAnticiposSearch,
} from '@/features/facturacion/lib/reportes-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Comparativa de anticipos vs aplicaciones y relación CFDI —
 * <c>/facturacion/reportes/comparativa-anticipos</c> (F6.2).
 */
export const Route = createFileRoute(
  '/_app/facturacion/reportes/comparativa-anticipos',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionReportesLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: ReporteComparativaAnticipos,
  validateSearch: (input: Record<string, unknown>): ComparativaAnticiposSearch =>
    ComparativaAnticiposSearchSchema.parse(input),
});
