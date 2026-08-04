import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { LoginScreen } from '@/components/auth/LoginScreen';

/**
 * Ruta pública <c>/login</c>. Si el usuario YA está autenticado, redirect a
 * <c>/</c> — evita que vea el LoginScreen tras refresh con sesión activa.
 */
export const Route = createFileRoute('/login')({
  beforeLoad: () => {
    const status = useAuthStore.getState().status;
    if (status === 'authenticated') {
      throw redirect({ to: '/' });
    }
  },
  component: LoginScreen,
});
