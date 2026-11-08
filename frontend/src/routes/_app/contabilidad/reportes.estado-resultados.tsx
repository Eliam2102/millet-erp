import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ReporteEstadoResultadosPage } from '@/features/contabilidad/pages/ReporteEstadoFinancieroPage';

/** Guard de UX: sin ContabilidadPolizasLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/reportes/estado-resultados')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  component: ReporteEstadoResultadosPage,
});
