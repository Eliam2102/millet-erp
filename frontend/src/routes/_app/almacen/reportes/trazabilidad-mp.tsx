import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteTrazabilidadMpPage } from '@/features/almacen/pages/ReporteTrazabilidadMpPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** Ruta F1-MP-09 — Trazabilidad de materia prima. */
export const Route = createFileRoute(
  '/_app/almacen/reportes/trazabilidad-mp',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenReportesAlfak)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ReporteTrazabilidadMpPage,
});
