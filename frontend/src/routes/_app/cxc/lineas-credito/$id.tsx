import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { LineasCreditoLayout } from '@/features/cxc/pages/LineasCreditoLayout';
import { DetalleLineaCredito } from '@/features/cxc/pages/DetalleLineaCredito';
import {
  LineasSearchSchema,
  type LineasSearch,
} from '@/features/cxc/lib/lineas-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Detalle de línea de crédito — <c>/cxc/lineas-credito/$id</c>
 * (CXC-FE-PR2): master-detail §6.1 (lista compacta 320px +
 * <c>&lt;DetalleLineaCredito/&gt;</c>; mobile drill-down). Hereda el
 * search-schema del index para preservar filtros.
 */
export const Route = createFileRoute('/_app/cxc/lineas-credito/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer)) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: DetalleRoute,
  validateSearch: (input: Record<string, unknown>): LineasSearch =>
    LineasSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/cxc/lineas-credito/$id' });
  return <LineasCreditoLayout idActivo={id} detalle={<DetalleLineaCredito />} />;
}
