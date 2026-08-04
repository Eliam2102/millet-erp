import { useCallback } from 'react';
import { useMsal } from '@azure/msal-react';
import { InteractionRequiredAuthError, type PublicClientApplication } from '@azure/msal-browser';
import { useAuthStore } from '@/lib/auth/auth-store';
import { authMode, apiScopes } from '@/lib/auth/config';
import { apiFetchJson } from '@/lib/auth/api-client';
import type { LoginResponse } from '@/lib/auth/types';

/**
 * Intercambia un access token de Entra por una sesión del API:
 * POST /api/auth/sesion → almacena el JWT del API en el store.
 */
async function exchangeEntraTokenForApiSession(entraToken: string): Promise<void> {
  const sessionResponse = await apiFetchJson<LoginResponse>('/api/auth/sesion', {
    method: 'POST',
    body: JSON.stringify({
      entraToken,
      empresaId: null,
    }),
  });
  useAuthStore.getState().setSession(sessionResponse);
}

/**
 * Procesa el response del redirect flow de MSAL. Si el usuario acaba de
 * volver de un loginRedirect, MSAL detecta el código de auth en la URL y
 * resuelve la promise con el AuthenticationResult. Si no hay redirect en
 * curso, resuelve con null.
 *
 * <para>Uso: AuthBootstrap llama esto en mount, ANTES de trySilentLogin.</para>
 *
 * Devuelve <c>true</c> si se restauró la sesión desde el redirect.
 */
export async function handlePostLoginRedirect(
  msalInstance: PublicClientApplication,
): Promise<boolean> {
  if (authMode !== 'EntraId') {
    return false;
  }

  const store = useAuthStore.getState();

  try {
    const result = await msalInstance.handleRedirectPromise();
    if (result === null) {
      return false; // No venimos de un redirect.
    }

    store.setStatus('authenticating');
    await exchangeEntraTokenForApiSession(result.accessToken);
    return true;
  } catch (err) {
    store.setStatus('error', err instanceof Error ? err.message : String(err));
    return false;
  }
}

/**
 * Intenta restaurar la sesión sin interacción usando MSAL silent token
 * acquisition. Llamado por <see cref="AuthBootstrap" /> tras
 * handlePostLoginRedirect: si MSAL ya tiene una cuenta cacheada (refresh
 * del browser con sesión activa), pide silent un access token para el API
 * y hace POST /api/auth/sesion. Restaura el store y el usuario salta
 * directo a la app.
 *
 * Devuelve <c>true</c> si la sesión se restauró, <c>false</c> si no había
 * cuenta o el silent falló (interaction_required → user debe hacer login).
 */
export async function trySilentLogin(
  msalInstance: PublicClientApplication,
): Promise<boolean> {
  if (authMode !== 'EntraId') {
    return false;
  }

  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) {
    return false;
  }

  const store = useAuthStore.getState();
  store.setStatus('authenticating');

  try {
    const tokenResult = await msalInstance.acquireTokenSilent({
      scopes: apiScopes,
      account: accounts[0],
    });

    await exchangeEntraTokenForApiSession(tokenResult.accessToken);
    return true;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      // Silent falló porque el token expiró sin refresh. Limpiar status,
      // user va a LoginScreen para login interactivo.
      store.setStatus('unauthenticated');
    } else {
      store.setStatus('error', err instanceof Error ? err.message : String(err));
    }
    return false;
  }
}

/**
 * Hook principal de auth. Combina:
 * - El store (estado: user, empresas, accessToken, etc.)
 * - Acciones de login/logout que dependen del modo (EntraId vs FakeForLocalDev)
 *
 * Para componentes que solo leen estado, usa <see cref="useAuthStore"/>
 * directamente con el selector apropiado para evitar re-renders innecesarios.
 */
export function useAuth() {
  const status = useAuthStore((s) => s.status);
  const errorMessage = useAuthStore((s) => s.errorMessage);
  const user = useAuthStore((s) => s.user);
  const empresas = useAuthStore((s) => s.empresas);
  const currentEmpresaId = useAuthStore((s) => s.currentEmpresaId);
  const setStatus = useAuthStore((s) => s.setStatus);
  const setSession = useAuthStore((s) => s.setSession);
  const updateEmpresas = useAuthStore((s) => s.updateEmpresas);
  const clearSession = useAuthStore((s) => s.clearSession);

  // useMsal devuelve un stub vacío cuando no hay MsalProvider en el árbol —
  // pero AuthBootstrap garantiza que SÍ haya provider en EntraId mode.
  // En FakeForLocalDev mode, msal nunca se invoca.
  const { instance: msalInstance } = useMsal();

  /**
   * Inicia sesión vía Entra ID con redirect flow. La ventana se navega a
   * Microsoft y vuelve después de la autenticación. handlePostLoginRedirect
   * (en AuthBootstrap) procesa el response al volver y popula el store.
   *
   * Razón del redirect en lugar de popup: browsers modernos restringen
   * cross-origin postMessage entre popup y opener (COOP), causando
   * timeouts de MSAL. Redirect flow no usa popup → no afecta.
   *
   * Solo válido en EntraId mode; en dev mode usar loginAsFakeUser.
   */
  const loginWithEntra = useCallback(async () => {
    if (authMode !== 'EntraId') {
      throw new Error('loginWithEntra solo disponible en modo EntraId.');
    }

    setStatus('authenticating');
    try {
      // Esto navega toda la ventana — el código de abajo NO se ejecuta.
      // El callback se procesa en AuthBootstrap → handlePostLoginRedirect
      // tras el redirect de Entra de regreso al SPA.
      await msalInstance.loginRedirect({ scopes: apiScopes });
    } catch (err) {
      setStatus('error', err instanceof Error ? err.message : String(err));
    }
  }, [msalInstance, setStatus]);

  /**
   * Login con un usuario seed de dev (modo FakeForLocalDev).
   * Llama directamente a /api/dev/fake-login con el oid sintético.
   */
  const loginAsFakeUser = useCallback(
    async (oid: string, email: string, nombre: string) => {
      if (authMode !== 'FakeForLocalDev') {
        throw new Error(
          'loginAsFakeUser solo disponible en modo FakeForLocalDev.',
        );
      }

      setStatus('authenticating');
      try {
        const sessionResponse = await apiFetchJson<LoginResponse>(
          '/api/dev/fake-login',
          {
            method: 'POST',
            body: JSON.stringify({
              entraOid: oid,
              email,
              nombre,
              empresaId: null,
            }),
          },
        );

        setSession(sessionResponse);
      } catch (err) {
        setStatus('error', err instanceof Error ? err.message : String(err));
      }
    },
    [setStatus, setSession],
  );

  /**
   * Cambia la empresa activa. Llama POST /api/auth/cambiar-empresa que
   * valida acceso y re-emite el JWT. La respuesta incluye la lista
   * actualizada con la nueva EsLaActual.
   */
  const changeEmpresa = useCallback(
    async (empresaId: string) => {
      const sessionResponse = await apiFetchJson<LoginResponse>(
        '/api/auth/cambiar-empresa',
        {
          method: 'POST',
          body: JSON.stringify({ empresaId }),
        },
      );
      setSession(sessionResponse);
      return sessionResponse;
    },
    [setSession],
  );

  /**
   * Cierra sesión: limpia state local y hace MSAL logoutRedirect si aplica.
   * El redirect flow es coherente con loginRedirect — toda la ventana
   * navega a Entra para invalidar la sesión y vuelve al SPA.
   */
  const logout = useCallback(async () => {
    clearSession();
    if (authMode === 'EntraId' && msalInstance.getAllAccounts().length > 0) {
      await msalInstance.logoutRedirect({ postLogoutRedirectUri: '/' });
    }
  }, [clearSession, msalInstance]);

  return {
    status,
    errorMessage,
    user,
    empresas,
    currentEmpresaId,
    currentEmpresa: empresas.find((e) => e.id === currentEmpresaId) ?? null,
    isAuthenticated: status === 'authenticated',
    isLoading: status === 'authenticating',
    loginWithEntra,
    loginAsFakeUser,
    changeEmpresa,
    logout,
    updateEmpresas,
  };
}
