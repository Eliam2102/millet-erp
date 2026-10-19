import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { useAuthStore } from '@/lib/auth/auth-store';

/** Herramienta interna: sólo aparece en desarrollo después de iniciar sesión y fuera de navegadores automatizados (Playwright), donde tapa botones. */
export function DevelopmentQueryDevtools() {
  const isAuthenticated = useAuthStore((state) => state.status === 'authenticated');

  if (!import.meta.env.DEV || !isAuthenticated || navigator.webdriver) {
    return null;
  }

  return <ReactQueryDevtools initialIsOpen={false} />;
}
