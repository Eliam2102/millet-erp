import { createFileRoute, redirect, useParams, useSearch } from '@tanstack/react-router';
import { MasterDetail } from '@/components/erp/MasterDetail';
import { ListaFacturasCompacta } from '@/features/cxp/components/ListasCompactasCxp';
import { FacturasSearchSchema, type FacturasSearch } from '@/features/cxp/lib/facturas-search-schema';
import { FacturaDetallePage } from '@/features/cxp/pages/FacturaDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P2 — Detalle de Factura (doc 07 §FE-F2-PR1).
 * Gate: <c>cuentas_por_pagar.facturas.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/facturas/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarFacturasLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: DetalleRoute,
  // Hereda el search de la bandeja para que «Volver» y la lista conserven filtros y página.
  validateSearch: (input: Record<string, unknown>): FacturasSearch => FacturasSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ strict: false }) as { id: string };
  const search = useSearch({ from: '/_app/cxp/facturas/$id' });
  return (
    <MasterDetail titulo="Facturas" bandejaTo="/cxp/facturas" bandejaSearch={search} lista={<ListaFacturasCompacta idActivo={id} search={search} />}>
      <FacturaDetallePage />
    </MasterDetail>
  );
}
