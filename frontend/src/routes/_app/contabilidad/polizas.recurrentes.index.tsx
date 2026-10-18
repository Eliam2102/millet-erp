import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PolizasRecurrentesPage } from '@/features/contabilidad/pages/PolizasRecurrentesPage';
import { RecurrentesSearchSchema, type RecurrentesSearch } from '@/features/contabilidad/lib/recurrentes';

/** Guard de UX: sin ContabilidadRecurrentesLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/polizas/recurrentes/')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadRecurrentesLeer)) throw redirect({ to: '/' });
  },
  validateSearch: (input: Record<string, unknown>): RecurrentesSearch => RecurrentesSearchSchema.parse(input),
  component: BandejaRoute,
});

function BandejaRoute() {
  const search = Route.useSearch();
  const navigate = Route.useNavigate();
  return (
    <PolizasRecurrentesPage
      search={search}
      onSearch={(next) => void navigate({ search: next, replace: true })}
      onAbrir={(id) => void navigate({ to: '/contabilidad/polizas/recurrentes/$id', params: { id }, search })}
    />
  );
}
