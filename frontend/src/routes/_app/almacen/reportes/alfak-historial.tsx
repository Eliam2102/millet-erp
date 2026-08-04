import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteAlfakPage } from '@/features/almacen/pages/ReporteAlfakPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** Ruta P14 — Reporte ALFAK-HISTORIAL-ALMACEN. */
export const Route = createFileRoute(
  '/_app/almacen/reportes/alfak-historial',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenReportesAlfak)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ReporteAlfakPage,
});
