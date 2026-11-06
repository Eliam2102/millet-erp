import { createFileRoute } from '@tanstack/react-router';
import { MisIncidenciasPage } from '@/features/incidencias/pages/MisIncidenciasPage';
import { IncidenciasSearchSchema, type IncidenciasSearch } from '@/features/incidencias/lib/search-schema';

/**
 * «Mis incidencias»: cualquier usuario autenticado ve las que tiene asignadas (el responsable no
 * necesita permiso; el servidor filtra por usuario).
 */
export const Route = createFileRoute('/_app/incidencias/')({
  component: MisIncidenciasPage,
  validateSearch: (input: Record<string, unknown>): IncidenciasSearch => IncidenciasSearchSchema.parse(input),
});
