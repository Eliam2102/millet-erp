import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { adminRegistry, adminEntryVisible } from '@/lib/admin/registry';
import { AdminLandingPage } from '@/components/admin/AdminLandingPage';

export const Route = createFileRoute('/_app/admin/')({
  beforeLoad: () => {
    if (
      !adminRegistry.some((entry) => adminEntryVisible(entry, useAuthStore.getState().permisos))
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: AdminLandingPage,
});
