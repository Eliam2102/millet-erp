import { useEffect } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Loader2, ShieldCheck } from 'lucide-react';
import { authMode } from '@/lib/auth/config';
import { useAuth } from '@/lib/auth/useAuth';
import { DevUserSelector } from '@/components/auth/DevUserSelector';
import { AuthErrorAlert } from '@/components/auth/AuthErrorAlert';
import { Badge } from '@/components/ui/badge';

/**
 * Pantalla de inicio de sesión de Millet ERP.
 * Renderiza el flujo corporativo según el modo configurado:
 * - EntraId: Botón oficial de Microsoft SSO con validación y estados de transición.
 * - FakeForLocalDev: Selector de usuarios de desarrollo (ADR-0015).
 */
export function LoginScreen() {
  const { loginWithEntra, logout, isLoading, errorMessage, status, isAuthenticated } = useAuth();
  const navigate = useNavigate();

  // Si el usuario ya está autenticado, redirigir a la raíz protegida.
  useEffect(() => {
    if (isAuthenticated) {
      navigate({ to: '/' });
    }
  }, [isAuthenticated, navigate]);

  return (
    <main className="min-h-screen flex items-center justify-center bg-gradient-to-b from-slate-50 to-slate-100 dark:from-slate-950 dark:to-slate-900 text-slate-900 dark:text-slate-100 p-4">
      <div className="w-full max-w-md space-y-6 rounded-xl border border-slate-200/80 dark:border-slate-800 bg-white/90 dark:bg-slate-900/90 backdrop-blur-xs p-8 shadow-md shadow-slate-200/50 dark:shadow-none">
        {/* Branding Header */}
        <header className="flex flex-col items-center text-center space-y-3">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-gradient-to-br from-indigo-600 via-purple-600 to-indigo-800 text-white shadow-md shadow-indigo-500/20">
            <span className="text-xl font-bold tracking-tight">M</span>
          </div>
          <div className="space-y-1">
            <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
              Millet ERP
            </h1>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Sistema de Gestión Empresarial
            </p>
          </div>
        </header>

        {/* Si ya está autenticado y esperando la navegación del router, mostrar splash de transición */}
        {isAuthenticated ? (
          <div className="flex flex-col items-center justify-center py-6 space-y-3 text-center">
            <Loader2 className="h-7 w-7 animate-spin text-indigo-600 dark:text-indigo-400" />
            <div className="space-y-0.5">
              <p className="text-sm font-medium text-slate-900 dark:text-slate-100">
                Sesión iniciada
              </p>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Cargando tu espacio de trabajo...
              </p>
            </div>
          </div>
        ) : authMode === 'EntraId' ? (
          <div className="space-y-4 pt-2">
            <button
              type="button"
              onClick={loginWithEntra}
              disabled={isLoading}
              className="w-full flex items-center justify-center gap-3 px-4 py-2.5 rounded-lg border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 font-medium text-sm shadow-xs hover:bg-slate-50 dark:hover:bg-slate-750 active:scale-[0.99] transition-all disabled:opacity-60 disabled:cursor-not-allowed cursor-pointer"
            >
              {isLoading ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin text-slate-500 dark:text-slate-400 shrink-0" />
                  <span>Conectando con Microsoft...</span>
                </>
              ) : (
                <>
                  {/* Logotipo oficial de 4 colores de Microsoft */}
                  <svg
                    className="h-4 w-4 shrink-0"
                    viewBox="0 0 21 21"
                    aria-hidden="true"
                  >
                    <rect x="1" y="1" width="9" height="9" fill="#F25022" />
                    <rect x="11" y="1" width="9" height="9" fill="#7FBA00" />
                    <rect x="1" y="11" width="9" height="9" fill="#00A4EF" />
                    <rect x="11" y="11" width="9" height="9" fill="#FFB900" />
                  </svg>
                  <span>Iniciar sesión con Microsoft</span>
                </>
              )}
            </button>

            {status === 'error' && (
              <button
                type="button"
                onClick={logout}
                disabled={isLoading}
                className="w-full text-center text-xs text-slate-500 hover:text-slate-900 dark:hover:text-slate-200 underline transition-colors cursor-pointer pt-1"
              >
                ¿Deseas cambiar de cuenta o cerrar sesión de Microsoft?
              </button>
            )}
          </div>
        ) : (
          <DevUserSelector />
        )}

        {/* Mensajes de error formateados RFC 7807 */}
        {status === 'error' && errorMessage && (
          <AuthErrorAlert error={errorMessage} />
        )}

        {/* Footer corporativo */}
        <footer className="text-xs text-slate-400 dark:text-slate-500 pt-4 border-t border-slate-100 dark:border-slate-800 flex items-center justify-center gap-1.5">
          {authMode === 'EntraId' ? (
            <>
              <ShieldCheck className="h-3.5 w-3.5 text-slate-400 dark:text-slate-500" />
              <span>Acceso protegido vía Microsoft Entra ID</span>
            </>
          ) : (
            <div className="flex items-center gap-1.5">
              <span>Modo desarrollo:</span>
              <Badge variant="outline" className="font-mono text-[10px] px-1.5 py-0">
                Mock Local
              </Badge>
            </div>
          )}
        </footer>
      </div>
    </main>
  );
}
