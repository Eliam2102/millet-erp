import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { MotorContablePage } from '@/features/contabilidad/pages/MotorContablePage';
import { MotorSearchSchema, type MotorSearch } from '@/features/contabilidad/lib/motor';

/** Guard de UX: sin ContabilidadMotorLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/motor')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadMotorLeer)) throw redirect({ to: '/' });
  },
  validateSearch: (input: Record<string, unknown>): MotorSearch => MotorSearchSchema.parse(input),
  component: MotorRoute,
});

function MotorRoute() {
  const search = Route.useSearch();
  const navigate = Route.useNavigate();
  return <MotorContablePage search={search} onSearch={(next) => void navigate({ search: next, replace: true })} />;
}
