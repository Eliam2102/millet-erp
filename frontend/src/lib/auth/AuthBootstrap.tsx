import { type ReactNode, useEffect, useState } from 'react';
import { MsalProvider } from '@azure/msal-react';
import { type PublicClientApplication } from '@azure/msal-browser';
import { authMode, buildMsalInstance } from '@/lib/auth/config';
import { handlePostLoginRedirect, trySilentLogin } from '@/lib/auth/useAuth';

interface AuthBootstrapProps {
  children: ReactNode;
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

  useEffect(() => {
    if (authMode !== 'EntraId') {
      return;
    }

    const instance = buildMsalInstance();
    if (instance === null) {
      return;
    }

    instance
      .initialize()
      .then(async () => {
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
        setMsalInstance(instance);
        setIsReady(true);
      })
      .catch((err) => {
        console.error('MSAL initialize failed:', err);
        setIsReady(true); // unblock UI; el LoginScreen mostrará el error
      });
  }, []);

  // Mientras MSAL inicializa, no renderizamos el árbol (evita usar el hook
  // useMsal sin provider). En FakeForLocalDev mode isReady=true desde el
  // arranque y este bloqueo no aplica.
  if (!isReady) {
    return (
      <div className="min-h-screen flex items-center justify-center text-slate-500">
        Inicializando...
      </div>
    );
  }

  // En FakeForLocalDev no creamos MsalProvider — useMsal devuelve stubs
  // pero useAuth solo invoca el path de MSAL si authMode === 'EntraId'.
  if (msalInstance === null) {
    return <>{children}</>;
  }

  return <MsalProvider instance={msalInstance}>{children}</MsalProvider>;
}
