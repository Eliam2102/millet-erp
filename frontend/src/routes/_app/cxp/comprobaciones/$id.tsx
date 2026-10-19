import { createFileRoute, redirect, useParams, useSearch } from '@tanstack/react-router';
import { MasterDetail } from '@/components/erp/MasterDetail';
import { ListaComprobacionesCompacta } from '@/features/cxp/components/ListasCompactasCxp';
import { ComprobacionesSearchSchema, type ComprobacionesSearch } from '@/features/cxp/lib/comprobaciones-search-schema';
import { ComprobacionDetallePage } from '@/features/cxp/pages/ComprobacionDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Detalle de Comprobación de Gastos (serie detalles CxP).
 * Gate: <c>cuentas_por_pagar.comprobaciones.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/comprobaciones/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CuentasPorPagarComprobacionesLeer)
    ) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: DetalleRoute,
  // Hereda el search de la bandeja para que «Volver» y la lista conserven filtros y página.
  validateSearch: (input: Record<string, unknown>): ComprobacionesSearch => ComprobacionesSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ strict: false }) as { id: string };
  const search = useSearch({ from: '/_app/cxp/comprobaciones/$id' });
  return (
    <MasterDetail titulo="Comprobaciones" bandejaTo="/cxp/comprobaciones" bandejaSearch={search} lista={<ListaComprobacionesCompacta idActivo={id} search={search} />}>
      <ComprobacionDetallePage />
    </MasterDetail>
  );
}
