import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteMpCnkPage } from '@/features/almacen/pages/ReporteMpCnkPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** Ruta P15 — Reporte SAP-REPORTE-EXISTENCIA-MP-CNK. */
export const Route = createFileRoute('/_app/almacen/reportes/mp-cnk')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenReportesMpCnk)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ReporteMpCnkPage,
});
