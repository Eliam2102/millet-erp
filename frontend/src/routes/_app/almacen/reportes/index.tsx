import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReportesPage } from '@/features/almacen/pages/ReportesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P13 — Hub de reportes de Almacén (doc 07 §FE-F6-PR1).
 * Gate: al menos uno de los permisos <c>almacen.reportes.*</c>.
 */
export const Route = createFileRoute('/_app/almacen/reportes/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const puede =
      permisos.includes(PermisosCanonicos.AlmacenReportesAlfak) ||
      permisos.includes(PermisosCanonicos.AlmacenReportesMpCnk);
    if (!puede) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ReportesPage,
});
