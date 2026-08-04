import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteLiquidacionCaja } from '@/features/facturacion/pages/ReporteLiquidacionCaja';
import {
  LiquidacionCajaSearchSchema,
  type LiquidacionCajaSearch,
} from '@/features/facturacion/lib/reportes-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Reporte Liquidación de caja —
 * <c>/facturacion/reportes/liquidacion-caja</c> (FE-F9).
 */
export const Route = createFileRoute(
  '/_app/facturacion/reportes/liquidacion-caja',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.FacturacionReportesLeer,
      PermisosCanonicos.FacturacionCajaLiquidar,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: ReporteLiquidacionCaja,
  validateSearch: (input: Record<string, unknown>): LiquidacionCajaSearch =>
    LiquidacionCajaSearchSchema.parse(input),
});
