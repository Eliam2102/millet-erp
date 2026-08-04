import { useEffect } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { authMode } from '@/lib/auth/config';
import { useAuth } from '@/lib/auth/useAuth';
import { DevUserSelector } from '@/components/auth/DevUserSelector';

/**
 * Pantalla de login. Renderiza el flujo apropiado según el modo:
 * - <c>EntraId</c>: botón único "Iniciar sesión" que dispara MSAL popup.
 * - <c>FakeForLocalDev</c>: lista de usuarios seed (DevUserSelector).
 *
 * Ambos paths terminan en el mismo estado: token en el store, app shell
 * renderizada por App.tsx.
 */
export function LoginScreen() {
  const { loginWithEntra, isLoading, errorMessage, status, isAuthenticated } = useAuth();
  const navigate = useNavigate();

  // El beforeLoad de la ruta solo corre al ENTRAR a la ruta. Cuando el
  // usuario ya está en /login y completa el login (DevUserSelector o
  // redirect de Entra), el status del store cambia pero el guard no
  // se re-ejecuta. Este effect compensa: si detectamos sesión, navega
  // explícitamente a la ruta protegida.
  useEffect(() => {
    if (isAuthenticated) {
      navigate({ to: '/' });
    }
  }, [isAuthenticated, navigate]);

  return (
    <main className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100 p-4">
      <div className="w-full max-w-md space-y-6 rounded-lg border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-6 shadow-sm">
        <header>
          <h1 className="text-2xl font-bold">Millet ERP</h1>
          <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">
            Inicia sesión para continuar.
          </p>
        </header>

        {authMode === 'EntraId' ? (
          <button
            onClick={loginWithEntra}
            disabled={isLoading}
            className="w-full rounded-md bg-slate-900 dark:bg-slate-100 text-white dark:text-slate-900 px-4 py-2.5 font-medium hover:bg-slate-800 dark:hover:bg-slate-200 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isLoading ? 'Iniciando sesión...' : 'Iniciar sesión con Microsoft'}
          </button>
        ) : (
          <DevUserSelector />
        )}

        {status === 'error' && errorMessage && (
          <div className="rounded-md bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-900 p-3 text-sm text-red-800 dark:text-red-200">
            <strong>Error:</strong> {errorMessage}
          </div>
        )}

        <footer className="text-xs text-slate-400 dark:text-slate-500 pt-4 border-t border-slate-100 dark:border-slate-800">
          Modo:{' '}
          <code className="font-mono bg-slate-100 dark:bg-slate-800 px-1 rounded">
            {authMode}
          </code>
        </footer>
      </div>
    </main>
  );
}
