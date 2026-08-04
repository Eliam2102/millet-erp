import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteEstadosAnticipos } from '@/features/facturacion/pages/ReporteEstadosAnticipos';
import {
  EstadosAnticiposSearchSchema,
  type EstadosAnticiposSearch,
} from '@/features/facturacion/lib/reportes-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Reporte Estados de facturas de anticipo —
 * <c>/facturacion/reportes/estados-anticipos</c> (FE-F9).
 */
export const Route = createFileRoute(
  '/_app/facturacion/reportes/estados-anticipos',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionReportesLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: ReporteEstadosAnticipos,
  validateSearch: (input: Record<string, unknown>): EstadosAnticiposSearch =>
    EstadosAnticiposSearchSchema.parse(input),
});
