import { createFileRoute, useParams, useSearch } from '@tanstack/react-router';
import { MasterDetail } from '@/components/erp/MasterDetail';
import { IncidenciaDetallePage, ListaIncidenciasCompacta } from '@/features/incidencias/pages/IncidenciaDetallePage';
import { IncidenciasSearchSchema, type IncidenciasSearch } from '@/features/incidencias/lib/search-schema';

/** Detalle de una incidencia con la lista compacta de «Mis incidencias» (el servidor valida el acceso). */
export const Route = createFileRoute('/_app/incidencias/$id')({
  component: DetalleRoute,
  validateSearch: (input: Record<string, unknown>): IncidenciasSearch => IncidenciasSearchSchema.parse(input),
});

function DetalleRoute() {
  const { id } = useParams({ strict: false }) as { id: string };
  const search = useSearch({ from: '/_app/incidencias/$id' });
  return (
    <MasterDetail
      titulo="Mis incidencias"
      bandejaTo="/incidencias"
      bandejaSearch={search}
      lista={<ListaIncidenciasCompacta idActivo={id} search={search} />}
    >
      <IncidenciaDetallePage />
    </MasterDetail>
  );
}
