import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SistemaDisenoPage } from '@/features/sistema-diseno/SistemaDisenoPage';

export const Route = createFileRoute('/_app/sistema-diseno/')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.AdminParametrosEditar)) {
      throw redirect({ to: '/' });
    }
  },
  component: SistemaDisenoPage,
});
