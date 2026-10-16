import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PolizasPage } from '@/features/contabilidad/pages/PolizasPage';
import { PolizasSearchSchema, type PolizasSearch } from '@/features/contabilidad/lib/polizas';

/** Guard de UX: sin ContabilidadPolizasLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/polizas/')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  validateSearch: (input: Record<string, unknown>): PolizasSearch => PolizasSearchSchema.parse(input),
  component: BandejaRoute,
});

function BandejaRoute() {
  const search = Route.useSearch();
  const navigate = Route.useNavigate();
  return (
    <PolizasPage
      search={search}
      onSearch={(next) => void navigate({ search: next, replace: true })}
      onAbrir={(id) => void navigate({ to: '/contabilidad/polizas/$id', params: { id }, search })}
    />
  );
}
