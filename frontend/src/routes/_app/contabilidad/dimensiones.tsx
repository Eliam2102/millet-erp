import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { DimensionesPage } from '@/features/contabilidad/pages/DimensionesPage';

/** Guard de UX: sin ContabilidadDimensionesLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/dimensiones')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadDimensionesLeer)) throw redirect({ to: '/' });
  },
  component: DimensionesPage,
});
