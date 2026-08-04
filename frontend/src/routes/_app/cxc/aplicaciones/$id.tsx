import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { AplicacionesLayout } from '@/features/cxc/pages/AplicacionesLayout';
import { DetalleAplicacion } from '@/features/cxc/pages/DetalleAplicacion';
import {
  AplicacionesSearchSchema,
  type AplicacionesSearch,
} from '@/features/cxc/lib/aplicaciones-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Detalle de propuesta — <c>/cxc/aplicaciones/$id</c> (CXC-FE-PR6):
 * master-detail §6.1 con acciones de Ingresos en el detalle. Hereda el
 * search-schema del index.
 */
export const Route = createFileRoute('/_app/cxc/aplicaciones/$id')({
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
  component: DetalleRoute,
  validateSearch: (input: Record<string, unknown>): AplicacionesSearch =>
    AplicacionesSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/cxc/aplicaciones/$id' });
  return <AplicacionesLayout idActivo={id} detalle={<DetalleAplicacion />} />;
}
