import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { DiferenciasFiscalesAwPage } from '@/modules/datos-maestros/components/DiferenciasFiscalesAwPage';

/**
 * <c>/admin/datos-maestros/diferencias-fiscales-aw</c> — reporte de
 * diferencias fiscales A+W vs ERP (U1.8b, F1-FAC-14).
 *
 * <para><b>Guard</b>: requiere
 * <c>datos_maestros.diferencias-fiscales-aw.leer</c>. Sin permiso,
 * redirige a <c>/</c>.</para>
 */
export const Route = createFileRoute('/_app/admin/datos-maestros/diferencias-fiscales-aw/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: DiferenciasFiscalesAwPage,
});
