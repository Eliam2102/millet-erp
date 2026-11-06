import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteKardexPage } from '@/features/almacen/pages/ReporteKardexPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** Ruta F1-ALM-15 — Kardex por artículo, ubicación y periodo. */
export const Route = createFileRoute('/_app/almacen/reportes/kardex')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenReportesAlfak)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ReporteKardexPage,
});
