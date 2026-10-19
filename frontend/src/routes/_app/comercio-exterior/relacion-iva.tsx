import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { RelacionIvaPage } from '@/features/comercio-exterior/pages/RelacionIvaPage';
export const Route = createFileRoute('/_app/comercio-exterior/relacion-iva')({
  beforeLoad: () => {
    if (
      !useAuthStore.getState().permisos.includes(PermisosCanonicos.ComercioExteriorRelacionIvaLeer)
    )
      throw redirect({ to: '/' });
  },
  component: RelacionIvaPage,
});
