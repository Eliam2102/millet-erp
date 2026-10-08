import { apiBaseUrl } from '@/lib/auth/config';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { MeResponse } from '@/lib/auth/types';

/**
 * Wrapper de <c>fetch</c> que inyecta <c>Authorization: Bearer &lt;jwt&gt;</c>
 * automáticamente desde el auth store. En 401, limpia la sesión local
 * (UI re-renderiza al LoginScreen). En 403 recarga los permisos vigentes
 * (U1.0) para que la UI oculte acciones retiradas sin esperar otro login.
 *
 * Uso: <c>apiFetch('/api/auth/me')</c> o <c>apiFetch('/api/auth/cambiar-empresa', { method: 'POST', body: JSON.stringify({...}) })</c>.
 */
export async function apiFetch(
  path: string,
  options: RequestInit = {},
): Promise<Response> {
  const token = useAuthStore.getState().accessToken;

  const headers = new Headers(options.headers);
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }
  // Solo los bodies string (JSON ya serializado por el caller) reciben
  // Content-Type: application/json por default. Los BodyInit nativos
  // definen su propio tipo — FormData necesita el multipart/boundary que
  // fija el browser (forzar JSON aquí rompía los uploads con 415), Blob
  // trae el suyo y URLSearchParams es form-urlencoded.
  if (typeof options.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const url = path.startsWith('http') ? path : `${apiBaseUrl}${path}`;
  const response = await fetch(url, { ...options, headers });

  if (response.status === 401) {
    // Token expiró o inválido. Limpiar sesión local; la UI vuelve a LoginScreen.
    // PR siguiente puede agregar silent refresh aquí si MSAL tiene cuenta activa.
    useAuthStore.getState().clearSession();
  }

  // Los endpoints de auth tienen su propio manejo (p. ej. 403 USUARIO_INACTIVO)
  // y /me es el propio refresco: excluirlos evita bucles.
  if (response.status === 403 && token && !path.includes('/api/auth/')) {
    void refrescarPermisos();
  }

  return response;
}

let refrescoEnCurso: Promise<void> | null = null;

/**
 * U1.0: recarga los permisos de la empresa actual desde <c>GET /api/auth/me</c>
 * (el backend ya invalidó su cache al cambiar el rol). Una sola petición
 * aunque lleguen varios 403 a la vez; si falla, el store queda como estaba.
 */
export function refrescarPermisos(): Promise<void> {
  refrescoEnCurso ??= (async () => {
    try {
      const response = await apiFetch('/api/auth/me');
      if (response.ok) {
        const me = (await response.json()) as MeResponse;
        useAuthStore.getState().updatePermisos(me.permisos);
      }
    } catch {
      // Best-effort: el backend sigue siendo quien autoriza.
    } finally {
      refrescoEnCurso = null;
    }
  })();
  return refrescoEnCurso;
}

/**
 * Helper que parsea JSON y lanza si la respuesta no es OK. Útil para
 * endpoints que siempre devuelven JSON.
 */
export async function apiFetchJson<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const response = await apiFetch(path, options);
  if (!response.ok) {
    const text = await response.text();
    throw new Error(
      `API ${response.status} ${response.statusText}: ${text || '(empty body)'}`,
    );
  }
  return (await response.json()) as T;
}
