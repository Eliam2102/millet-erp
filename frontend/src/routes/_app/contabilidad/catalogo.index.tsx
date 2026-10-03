import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CatalogoPage } from '@/features/contabilidad/pages/CatalogoPage';

/** Guard de UX: sin ContabilidadCatalogoLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/catalogo/')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadCatalogoLeer)) throw redirect({ to: '/' });
  },
  component: CatalogoPage,
});
