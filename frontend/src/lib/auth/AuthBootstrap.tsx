import { type ReactNode, useEffect, useState } from 'react';
import { MsalProvider } from '@azure/msal-react';
import { type PublicClientApplication } from '@azure/msal-browser';
import { authMode, buildMsalInstance } from '@/lib/auth/config';
import { handlePostLoginRedirect, trySilentLogin } from '@/lib/auth/useAuth';
import { GlassLoader } from '@/components/auth/GlassLoader';

interface AuthBootstrapProps {
  children: ReactNode;
}

/**
 * Arranque de MSAL compartido por todo el módulo. React StrictMode monta los
 * efectos dos veces en desarrollo; sin esto se creaban dos instancias y cada
 * una procesaba el mismo redirect, mandando dos POST /api/auth/sesion en
 * paralelo (el segundo chocaba con el alta del usuario → 500).
 */
let arranqueMsal: Promise<PublicClientApplication | null> | null = null;

function arrancarMsal(): Promise<PublicClientApplication | null> {
  arranqueMsal ??= (async () => {
    const instance = buildMsalInstance();
    if (instance === null) {
      return null;
    }
    await instance.initialize();
    // Orden importante:
    // 1. handlePostLoginRedirect: si acabamos de volver de Entra (redirect
    //    flow), MSAL detecta el código en la URL y restaura la sesión.
    // 2. trySilentLogin: si NO veníamos de un redirect pero MSAL tiene
    //    cuenta cacheada (refresh del browser con sesión activa), pide
    //    silent un access token y restaura la sesión.
    // Si ambos fallan, queda 'unauthenticated' y se muestra LoginScreen.
    const restoredFromRedirect = await handlePostLoginRedirect(instance);
    if (!restoredFromRedirect) {
      await trySilentLogin(instance);
    }
    return instance;
  })().catch((error: unknown) => {
    // Permite reintentar un fallo temporal de inicialización.
    arranqueMsal = null;
    throw error;
  });
  return arranqueMsal;
}

/**
 * Wrapper que envuelve el árbol con MsalProvider en modo EntraId. En modo
 * FakeForLocalDev pasa los hijos sin envolver — pero usa un MsalProvider
 * "stub" con una instancia mínima para que <see cref="useMsal"/> no falle
 * (siempre necesita un provider en el árbol; el stub nunca se activa).
 *
 * Razón del stub: useAuth() llama a useMsal() incondicionalmente (las
 * reglas de hooks no permiten condicionales). En FakeForLocalDev mode los
 * caminos de MSAL nunca se ejecutan, pero el provider debe existir.
 */
export function AuthBootstrap({ children }: AuthBootstrapProps) {
  const [msalInstance, setMsalInstance] = useState<PublicClientApplication | null>(
    null,
  );
  const [isReady, setIsReady] = useState(authMode !== 'EntraId');
  const [startupError, setStartupError] = useState(false);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (authMode !== 'EntraId') {
      return;
    }

    let activo = true;
    arrancarMsal()
      .then((instance) => {
        if (!activo || instance === null) {
          return;
        }
        setMsalInstance(instance);
        setIsReady(true);
      })
      .catch((err) => {
        console.error('MSAL initialize failed:', err);
        if (activo) {
          setStartupError(true);
        }
      });

    return () => {
      activo = false;
    };
  }, [attempt]);

  if (startupError) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-4 bg-slate-950 p-6 text-center text-white" role="alert">
        <p>No se pudo iniciar el acceso. Comprueba tu conexión e inténtalo de nuevo.</p>
        <button
          type="button"
          className="rounded-md bg-white px-4 py-2 font-medium text-slate-900"
          onClick={() => {
            setStartupError(false);
            setAttempt((current) => current + 1);
          }}
        >
          Reintentar
        </button>
      </div>
    );
  }

  // Mientras MSAL inicializa, no renderizamos el árbol (evita usar el hook
  // useMsal sin provider). En FakeForLocalDev mode isReady=true desde el
  // arranque y este bloqueo no aplica.
  if (!isReady) {
    return <GlassLoader />;
  }

  // En FakeForLocalDev no creamos MsalProvider — useMsal devuelve stubs
  // pero useAuth solo invoca el path de MSAL si authMode === 'EntraId'.
  if (msalInstance === null) {
    return <>{children}</>;
  }

  return <MsalProvider instance={msalInstance}>{children}</MsalProvider>;
}
