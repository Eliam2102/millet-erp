import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ImpuestosPage } from '@/modules/catalogos/components/ImpuestosPage';

export const Route = createFileRoute('/_app/admin/catalogos/impuestos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer) &&
        !permisos.includes(PermisosCanonicos.CompartidoCatalogosAdministrar)) {
      throw redirect({ to: '/' });
    }
  },
  component: ImpuestosPage,
});
