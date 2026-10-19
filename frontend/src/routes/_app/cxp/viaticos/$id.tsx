import { createFileRoute, redirect, useParams, useSearch } from '@tanstack/react-router';
import { MasterDetail } from '@/components/erp/MasterDetail';
import { ListaViaticosCompacta } from '@/features/cxp/components/ListasCompactasCxp';
import { ViaticosSearchSchema, type ViaticosSearch } from '@/features/cxp/lib/viaticos-search-schema';
import { ViaticoDetallePage } from '@/features/cxp/pages/ViaticoDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Detalle de Solicitud de Viáticos (serie detalles CxP).
 * Gate: <c>cuentas_por_pagar.viaticos.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/viaticos/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarViaticosLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: DetalleRoute,
  // Hereda el search de la bandeja para que «Volver» y la lista conserven filtros y página.
  validateSearch: (input: Record<string, unknown>): ViaticosSearch => ViaticosSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ strict: false }) as { id: string };
  const search = useSearch({ from: '/_app/cxp/viaticos/$id' });
  return (
    <MasterDetail titulo="Viáticos" bandejaTo="/cxp/viaticos" bandejaSearch={search} lista={<ListaViaticosCompacta idActivo={id} search={search} />}>
      <ViaticoDetallePage />
    </MasterDetail>
  );
}
