import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PeriodosPage } from '@/features/contabilidad/pages/PeriodosPage';

/** Guard de UX: sin ContabilidadPeriodoLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/periodos')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPeriodoLeer)) throw redirect({ to: '/' });
  },
  component: PeriodosPage,
});
