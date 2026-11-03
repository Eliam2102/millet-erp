import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ReporteDiarioPage } from '@/features/contabilidad/pages/ReporteDiarioPage';

/** Guard de UX: sin ContabilidadPolizasLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/reportes/diario')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  component: ReporteDiarioPage,
});
