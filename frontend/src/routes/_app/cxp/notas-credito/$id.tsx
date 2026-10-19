import { createFileRoute, redirect, useParams, useSearch } from '@tanstack/react-router';
import { MasterDetail } from '@/components/erp/MasterDetail';
import { ListaNotasCreditoCompacta } from '@/features/cxp/components/ListasCompactasCxp';
import { NotasCreditoSearchSchema, type NotasCreditoSearch } from '@/features/cxp/lib/notas-y-anticipos-search-schema';
import { NotaCreditoDetallePage } from '@/features/cxp/pages/NotaCreditoDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Detalle de Nota de Crédito (serie detalles CxP).
 * Gate: <c>cuentas_por_pagar.notas-credito.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/notas-credito/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarNotasCreditoLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: DetalleRoute,
  // Hereda el search de la bandeja para que «Volver» y la lista conserven filtros y página.
  validateSearch: (input: Record<string, unknown>): NotasCreditoSearch => NotasCreditoSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ strict: false }) as { id: string };
  const search = useSearch({ from: '/_app/cxp/notas-credito/$id' });
  return (
    <MasterDetail titulo="Notas de crédito" bandejaTo="/cxp/notas-credito" bandejaSearch={search} lista={<ListaNotasCreditoCompacta idActivo={id} search={search} />}>
      <NotaCreditoDetallePage />
    </MasterDetail>
  );
}
