import { createFileRoute, redirect, useParams, useSearch } from '@tanstack/react-router';
import { MasterDetail } from '@/components/erp/MasterDetail';
import { ListaNotasCargoCompacta } from '@/features/cxp/components/ListasCompactasCxp';
import { NotasCargoSearchSchema, type NotasCargoSearch } from '@/features/cxp/lib/notas-y-anticipos-search-schema';
import { NotaCargoDetallePage } from '@/features/cxp/pages/NotaCargoDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Detalle de Nota de Cargo (serie detalles CxP).
 * Gate: <c>cuentas_por_pagar.notas-cargo.leer</c>.
 */
export const Route = createFileRoute('/_app/cxp/notas-cargo/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CuentasPorPagarNotasCargoLeer)) {
      throw redirect({ to: '/cxp' });
    }
  },
  component: DetalleRoute,
  // Hereda el search de la bandeja para que «Volver» y la lista conserven filtros y página.
  validateSearch: (input: Record<string, unknown>): NotasCargoSearch => NotasCargoSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ strict: false }) as { id: string };
  const search = useSearch({ from: '/_app/cxp/notas-cargo/$id' });
  return (
    <MasterDetail titulo="Notas de cargo" bandejaTo="/cxp/notas-cargo" bandejaSearch={search} lista={<ListaNotasCargoCompacta idActivo={id} search={search} />}>
      <NotaCargoDetallePage />
    </MasterDetail>
  );
}
