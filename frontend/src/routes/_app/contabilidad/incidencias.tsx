import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { IncidenciasPage } from '@/features/contabilidad/pages/IncidenciasPage';
import { IncidenciasSearchSchema, type IncidenciasSearch } from '@/features/contabilidad/lib/incidencias';

/** Guard de UX: sin ContabilidadPolizasLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/incidencias')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  validateSearch: (input: Record<string, unknown>): IncidenciasSearch => IncidenciasSearchSchema.parse(input),
  component: IncidenciasRoute,
});

function IncidenciasRoute() {
  const search = Route.useSearch();
  const navigate = Route.useNavigate();
  return <IncidenciasPage search={search} onSearch={(next) => void navigate({ search: next, replace: true })} />;
}
