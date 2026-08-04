import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { AuditoriaPage } from '@/modules/administracion/components/AuditoriaPage';

/**
 * <c>/admin/auditoria</c> — bandeja P2 (server-side filters) del log
 * consolidado <c>core.audit_log</c> (UF-Admin-PR7 §1).
 *
 * <para><b>Guard</b>: requiere <c>admin.auditoria.leer</c>. Sin
 * permiso, redirige a <c>/</c> — el gear del Topbar ya hace el mismo
 * gate; la URL directa también debe estar protegida.</para>
 */
export const Route = createFileRoute('/_app/admin/auditoria/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AdminAuditoriaLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: AuditoriaIndexRoute,
});

function AuditoriaIndexRoute() {
  return <AuditoriaPage />;
}
