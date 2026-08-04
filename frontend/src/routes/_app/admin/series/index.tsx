import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SeriesPage } from '@/modules/administracion/components/SeriesPage';
import { NuevaSerieProvider } from '@/modules/administracion/components/SheetNuevaSerie';

/**
 * <c>/admin/series</c> — bandeja P1 (full-width tabular) de Series y
 * folios (UF-Admin-PR6). Filtros server-side por Empresa y Tipo de
 * Documento; edición inline desde la fila con AlertDialog confirm al
 * cambiar el reinicio del periodo.
 *
 * <para><b>Guard</b>: requiere <c>admin.series.gestionar</c>. Sin
 * permiso, redirige a <c>/</c> — el gear del Topbar ya hace el mismo
 * gate; la URL directa también debe estar protegida.</para>
 */
export const Route = createFileRoute('/_app/admin/series/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminSeriesGestionar)) {
      throw redirect({ to: '/' });
    }
  },
  component: SeriesIndexRoute,
});

function SeriesIndexRoute() {
  return (
    <NuevaSerieProvider>
      <SeriesPage />
    </NuevaSerieProvider>
  );
}
