import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { UsosCfdiPage } from '@/modules/catalogos/components/UsosCfdiPage';

/**
 * <c>/admin/catalogos/usos-cfdi</c> — read-only SAT (UF-Admin-PR5.3).
 */
export const Route = createFileRoute('/_app/admin/catalogos/usos-cfdi/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer)) {
      throw redirect({ to: '/' });
    }
  },
  component: UsosCfdiPage,
});
