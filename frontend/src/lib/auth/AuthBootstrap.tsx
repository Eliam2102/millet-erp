import { type ReactNode, useEffect, useState } from 'react';
import { MsalProvider } from '@azure/msal-react';
import { type PublicClientApplication } from '@azure/msal-browser';
import { authMode, buildMsalInstance } from '@/lib/auth/config';
import { handlePostLoginRedirect, trySilentLogin } from '@/lib/auth/useAuth';
import { Loader2 } from 'lucide-react';

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
      <div className="min-h-screen flex flex-col items-center justify-center bg-gradient-to-b from-slate-50 to-slate-100 dark:from-slate-950 dark:to-slate-900 text-slate-900 dark:text-slate-100 p-4">
        <div className="flex flex-col items-center space-y-4">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-gradient-to-br from-indigo-600 via-purple-600 to-indigo-800 text-white shadow-md shadow-indigo-500/20">
            <span className="text-xl font-bold tracking-tight">M</span>
          </div>
          <div className="flex items-center gap-2 text-sm text-slate-500 dark:text-slate-400">
            <Loader2 className="h-4 w-4 animate-spin text-indigo-600 dark:text-indigo-400" />
            <span>Iniciando Millet ERP...</span>
          </div>
        </div>
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
