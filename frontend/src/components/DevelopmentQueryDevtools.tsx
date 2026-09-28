import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { useAuthStore } from '@/lib/auth/auth-store';

/** Herramienta interna: sólo aparece en desarrollo después de iniciar sesión. */
export function DevelopmentQueryDevtools() {
  const isAuthenticated = useAuthStore((state) => state.status === 'authenticated');

  if (!import.meta.env.DEV || !isAuthenticated) {
    return null;
  }

  return <ReactQueryDevtools initialIsOpen={false} />;
}
