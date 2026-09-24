import { useCallback } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useMsal } from '@azure/msal-react';
import { InteractionRequiredAuthError, type PublicClientApplication } from '@azure/msal-browser';
import { useAuthStore } from '@/lib/auth/auth-store';
import { authMode, apiScopes } from '@/lib/auth/config';
import { apiFetchJson } from '@/lib/auth/api-client';
import { queryClient } from '@/lib/query-client';
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
 * @returns true si se procesó un redirect exitosamente; false si no había redirect.
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
    if (!result) {
      return false; // No venimos de un redirect.
    }

    if (result.account) {
      msalInstance.setActiveAccount(result.account);
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
 * Intenta autenticar silenciosamente si ya hay una cuenta en MSAL cache:
 * 1. acquireTokenSilent con la primera cuenta en cache.
 * 2. Si OK: exchangeEntraTokenForApiSession → status 'authenticated'.
 * 3. Si InteractionRequiredAuthError (token expirado/sin refresh):
 *    status 'unauthenticated' → UI muestra botón "Iniciar sesión".
 * 4. Si otro error: status 'error'.
 *
 * <para>Uso: AuthBootstrap llama esto en mount si handlePostLoginRedirect
 * devolvió false.</para>
 */
export async function trySilentLogin(
  msalInstance: PublicClientApplication,
): Promise<boolean> {
  if (authMode !== 'EntraId') {
    return false;
  }

  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) {
    useAuthStore.getState().setStatus('unauthenticated');
    return false;
  }

  const account = msalInstance.getActiveAccount() ?? accounts[0];
  msalInstance.setActiveAccount(account);

  const store = useAuthStore.getState();
  store.setStatus('authenticating');

  try {
    const tokenResult = await msalInstance.acquireTokenSilent({
      scopes: apiScopes,
      account,
    });

    await exchangeEntraTokenForApiSession(tokenResult.accessToken);
    return true;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      // Silent falló porque el token expiró sin refresh. Limpiar status,
      // user va a LoginScreen para login interactivo.
      store.setStatus('unauthenticated');
    } else {
      // Si el backend rechazó la cuenta (ej. 403 USUARIO_INACTIVO),
      // removemos la cuenta activa de MSAL para no reintentar
      // silent login con la misma cuenta inactiva en cada refresh.
      msalInstance.setActiveAccount(null);
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
  const isSwitchingEmpresa = useAuthStore((s) => s.isSwitchingEmpresa);
  const setStatus = useAuthStore((s) => s.setStatus);
  const setSession = useAuthStore((s) => s.setSession);
  const updateEmpresas = useAuthStore((s) => s.updateEmpresas);
  const setIsSwitchingEmpresa = useAuthStore((s) => s.setIsSwitchingEmpresa);
  const clearSession = useAuthStore((s) => s.clearSession);
  const navigate = useNavigate();

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
      // prompt: 'select_account' fuerza a Microsoft a mostrar la pantalla de
      // selección de cuenta, permitiendo cambiar de cuenta incluso si ya hay
      // una sesión activa en el navegador (evita bucles con cuentas inactivas).
      await msalInstance.loginRedirect({
        scopes: apiScopes,
        prompt: 'select_account',
      });
    } catch (err) {
      setStatus('error', err instanceof Error ? err.message : String(err));
    }
  }, [msalInstance, setStatus]);

  /**
   * Login simulado para desarrollo local (ADR-0015).
   * Llama POST /api/dev/fake-login que genera un JWT interno sin Entra.
   */
  const loginAsFakeUser = useCallback(
    async (entraOid: string, email: string, nombre: string) => {
      setStatus('authenticating');
      try {
        const sessionResponse = await apiFetchJson<LoginResponse>(
          '/api/dev/fake-login',
          {
            method: 'POST',
            body: JSON.stringify({
              entraOid,
              email,
              nombre,
              empresaId: null,
            }),
          },
        );

        // El modo dev permite cambiar de usuario sin recargar la pestaña.
        // Ninguna respuesta cacheada del administrador debe sobrevivir al
        // cambio hacia el colaborador de prueba.
        queryClient.clear();
        setSession(sessionResponse);
        navigate({ to: '/' });
      } catch (err) {
        setStatus('error', err instanceof Error ? err.message : String(err));
      }
    },
    [setStatus, setSession, navigate],
  );

  /**
   * Cambia la empresa activa. Llama POST /api/auth/cambiar-empresa que
   * valida acceso y re-emite el JWT. La respuesta incluye la lista
   * actualizada con la nueva EsLaActual.
   *
   * F1-ADM-01 Fase 3: limpia el cache de TanStack Query después de aplicar
   * la nueva sesión — sin esto, un componente que no vuelve a montar sigue
   * mostrando datos servidos con el `current_empresa_id` anterior hasta que
   * su `staleTime` expira, mezclando visualmente información entre empresas.
   * `clear()` (no solo `invalidateQueries`) también descarta queries en
   * cache que ningún componente activo está pidiendo en este momento.
   */
  const changeEmpresa = useCallback(
    async (empresaId: string) => {
      setIsSwitchingEmpresa(true);
      try {
        const sessionResponse = await apiFetchJson<LoginResponse>(
          '/api/auth/cambiar-empresa',
          {
            method: 'POST',
            body: JSON.stringify({ empresaId }),
          },
        );
        setSession(sessionResponse);
        // Limpia el cache de consultas para que todos los módulos recarguen
        // los datos y catálogos de la nueva empresa sin mezclar estados
        queryClient.clear();
        // Breve pausa para brindar feedback visual suave y evitar parpadeo brusco
        await new Promise((resolve) => setTimeout(resolve, 300));
        return sessionResponse;
      } finally {
        setIsSwitchingEmpresa(false);
      }
    },
    [setSession, setIsSwitchingEmpresa],
  );

  /**
   * Cierra sesión: limpia state local y hace MSAL logoutRedirect si aplica.
   * El redirect flow es coherente con loginRedirect — toda la ventana
   * navega a Entra para invalidar la sesión y vuelve al SPA.
   *
   * También limpia el cache de TanStack Query: sin esto, un login
   * subsiguiente (mismo tab, otro usuario/empresa en dev con
   * DevUserSelector) podría mostrar por un instante datos cacheados de la
   * sesión anterior antes de que las queries se vuelvan a disparar.
   */
  const logout = useCallback(async () => {
    const account =
      msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0];
    if (authMode === 'EntraId' && account) {
      // Limpia la sesión en memoria pero mantiene status 'authenticating'
      // para que cualquier pantalla intermedia muestre estado de carga y deshabilite
      // botones mientras el navegador redirige a Microsoft Entra ID.
      clearSession('authenticating');
      await msalInstance.logoutRedirect({
        account,
        postLogoutRedirectUri: window.location.origin + '/',
      });
    } else {
      clearSession('idle');
      navigate({ to: '/login' });
    }
  }, [clearSession, msalInstance, navigate]);

  return {
    status,
    errorMessage,
    user,
    empresas,
    currentEmpresaId,
    currentEmpresa: empresas.find((e) => e.id === currentEmpresaId) ?? null,
    isAuthenticated: status === 'authenticated',
    isLoading: status === 'authenticating',
    isSwitchingEmpresa,
    loginWithEntra,
    loginAsFakeUser,
    changeEmpresa,
    logout,
    updateEmpresas,
  };
}
