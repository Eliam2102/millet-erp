import { useEffect } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { Loader2 } from 'lucide-react';
import { authMode } from '@/lib/auth/config';
import { useAuth } from '@/lib/auth/useAuth';
import { DevUserSelector } from '@/components/auth/DevUserSelector';
import { AuthErrorAlert } from '@/components/auth/AuthErrorAlert';
import './login.css';

export function LoginScreen() {
  const { loginWithEntra, logout, isLoading, errorMessage, status, isAuthenticated } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    if (isAuthenticated) {
      navigate({ to: '/' });
    }
  }, [isAuthenticated, navigate]);

  return (
    <main className="millet-login">
      <section className="millet-login__brand" aria-label="Millet, industria de vidrio">
        <div className="millet-login__wordmark" aria-hidden="true">
          <span>MILLET</span>
          <small>INDUSTRIA DE VIDRIO</small>
        </div>
        <div className="millet-login__glass" aria-hidden="true">
          <span />
          <span />
          <span />
        </div>
      </section>

      <section className="millet-login__content" aria-labelledby="millet-login-title">
        <div className="millet-login__form">
          <h1 id="millet-login-title">Iniciar sesión</h1>
          <p className="millet-login__intro">Continúa con tu cuenta de trabajo.</p>

          {isAuthenticated ? (
            <div className="millet-login__transition" role="status">
              <Loader2 className="h-5 w-5 animate-spin" aria-hidden="true" />
              <span>Abriendo tu espacio de trabajo…</span>
            </div>
          ) : authMode === 'EntraId' ? (
            <>
              <button
                type="button"
                onClick={() => void loginWithEntra()}
                disabled={isLoading}
                className="millet-login__microsoft"
              >
                {isLoading ? (
                  <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                ) : (
                  <svg className="h-4 w-4 shrink-0" viewBox="0 0 21 21" aria-hidden="true">
                    <rect x="1" y="1" width="9" height="9" fill="#F25022" />
                    <rect x="11" y="1" width="9" height="9" fill="#7FBA00" />
                    <rect x="1" y="11" width="9" height="9" fill="#00A4EF" />
                    <rect x="11" y="11" width="9" height="9" fill="#FFB900" />
                  </svg>
                )}
                <span>{isLoading ? 'Conectando con Microsoft…' : 'Continuar con Microsoft'}</span>
              </button>
              {status === 'error' && (
                <button
                  type="button"
                  onClick={() => void logout()}
                  disabled={isLoading}
                  className="millet-login__switch-account"
                >
                  Cambiar de cuenta Microsoft
                </button>
              )}
            </>
          ) : (
            <DevUserSelector />
          )}

          {status === 'error' && errorMessage && (
            <AuthErrorAlert error={errorMessage} className="mt-5" />
          )}

          <p className="millet-login__help">
            {authMode === 'EntraId'
              ? '¿No puedes ingresar? Contacta al área de TI.'
              : 'Entorno local de pruebas. Selecciona una cuenta para continuar.'}
          </p>
        </div>
      </section>
    </main>
  );
}
