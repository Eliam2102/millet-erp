import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ReporteConciliacionAuxiliaresPage } from '@/features/contabilidad/pages/ReporteConciliacionAuxiliaresPage';

/** F1-CON-09. Guard de UX: sin ContabilidadPolizasLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/reportes/conciliacion-auxiliares')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  component: ReporteConciliacionAuxiliaresPage,
});
