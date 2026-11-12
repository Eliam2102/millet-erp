import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReporteConsumoInsumosLineaPage } from '@/features/almacen/pages/ReporteConsumoInsumosLineaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** Ruta M-339 — Consumo de insumos por línea. */
export const Route = createFileRoute(
  '/_app/almacen/reportes/consumo-insumos-linea',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenReportesAlfak)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ReporteConsumoInsumosLineaPage,
});
