import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PaqueteAuditorPage } from '@/features/contabilidad/pages/PaqueteAuditorPage';

/** Guard de UX: sin ContabilidadPolizasLeer se redirige; cada reporte enlazado conserva su propio permiso. */
export const Route = createFileRoute('/_app/contabilidad/reportes/auditor')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  component: PaqueteAuditorPage,
});
