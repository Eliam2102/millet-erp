import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ImportacionPage } from '@/features/contabilidad/pages/ImportacionPage';

/** Guard de UX: sin ContabilidadCatalogoImportar se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/importacion')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadCatalogoImportar)) throw redirect({ to: '/' });
  },
  component: ImportacionPage,
});
