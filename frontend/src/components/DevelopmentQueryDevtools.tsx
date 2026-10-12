import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * Herramienta interna de TanStack Query Devtools. Es opt-in: sólo aparece si
 * corre en modo DEV, el usuario ya inició sesión y `VITE_QUERY_DEVTOOLS` vale
 * `'true'`. Para activarla en tu máquina, agrega `VITE_QUERY_DEVTOOLS=true` a
 * `.env.development.local` (no se versiona). Sin la variable no se renderiza
 * nada, para no tapar botones de los paneles en la demo.
 */
export function DevelopmentQueryDevtools() {
  const isAuthenticated = useAuthStore((state) => state.status === 'authenticated');

  if (!import.meta.env.DEV || import.meta.env.VITE_QUERY_DEVTOOLS !== 'true' || !isAuthenticated) {
    return null;
  }

  return <ReactQueryDevtools initialIsOpen={false} />;
}
