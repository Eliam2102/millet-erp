import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PlantillaRecurrenteDetallePage } from '@/features/contabilidad/pages/PlantillaRecurrenteDetallePage';
import { RecurrentesSearchSchema, type RecurrentesSearch } from '@/features/contabilidad/lib/recurrentes';

/** Guard de UX: sin ContabilidadRecurrentesLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/polizas/recurrentes/$id')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadRecurrentesLeer)) throw redirect({ to: '/' });
  },
  validateSearch: (input: Record<string, unknown>): RecurrentesSearch => RecurrentesSearchSchema.parse(input),
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = Route.useParams();
  const search = Route.useSearch();
  return <PlantillaRecurrenteDetallePage id={id} search={search} />;
}
