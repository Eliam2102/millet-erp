import type { PublicClientApplication } from '@azure/msal-browser';
import { apiBaseUrl, apiScopes, authMode } from '@/lib/auth/config';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { LoginResponse } from '@/lib/auth/types';

/**
 * 86e3x819w — renovación silenciosa del token del ERP.
 *
 * El JWT del API dura poco (15 min, Auth:Jwt:AccessTokenLifetimeMinutes) para
 * que un token copiado (XSS o equipo compartido) sirva sólo unos minutos.
 * Para que eso no saque al usuario a mitad de un formulario, antes de que
 * venza se pide a MSAL un token de Entra en silencio (usa el refresh token de
 * su cache en sessionStorage, sin ventana) y se cambia por una sesión nueva del
 * API con la MISMA empresa. El store sólo actualiza token, vencimiento y
 * permisos (renovarToken): empresa, sucursal y pantalla no se mueven.
 *
 * Sólo aplica en modo EntraId. En FakeForLocalDev no hay MSAL: al vencer, el
 * 401 lleva al login como antes.
 */

/** Cuánto antes del vencimiento se renueva. */
export const MARGEN_RENOVACION_MS = 2 * 60 * 1000;
/** Espera mínima entre programaciones, para no renovar en ráfaga si el reloj está desfasado. */
export const ESPERA_MINIMA_MS = 10 * 1000;

let msal: PublicClientApplication | null = null;
let renovacionEnCurso: Promise<boolean> | null = null;
let temporizador: ReturnType<typeof setTimeout> | null = null;
let dejarDeEscuchar: (() => void) | null = null;

/** AuthBootstrap la registra al terminar de iniciar MSAL. */
export function registrarInstanciaMsal(instancia: PublicClientApplication | null): void {
  msal = instancia;
}

/**
 * Pide un token nuevo del ERP sin interacción. Una sola petición aunque la
 * pidan varias llamadas a la vez. Devuelve false si no se pudo (sin MSAL, sin
 * cuenta, MSAL exige interacción o el API rechazó): quien llama decide si
 * manda al login.
 */
export function renovarSesion(): Promise<boolean> {
  // El finally se encadena DESPUÉS de guardar la promesa: si renovar termina
  // sin ningún await (sin MSAL o sin cuenta) y el finally viviera dentro de la
  // función async, limpiaría antes de la asignación y la promesa ya resuelta
  // quedaría guardada para siempre, bloqueando toda renovación posterior.
  renovacionEnCurso ??= renovar().finally(() => {
    renovacionEnCurso = null;
  });
  return renovacionEnCurso;
}

async function renovar(): Promise<boolean> {
  try {
    if (authMode !== 'EntraId' || msal === null) return false;
    const cuenta = msal.getActiveAccount() ?? msal.getAllAccounts()[0];
    if (!cuenta) return false;

    const entra = await msal.acquireTokenSilent({ scopes: apiScopes, account: cuenta });
    const empresaId = useAuthStore.getState().currentEmpresaId;
    const respuesta = await fetch(`${apiBaseUrl}/api/auth/sesion`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ entraToken: entra.accessToken, empresaId }),
    });
    if (!respuesta.ok) return false;

    useAuthStore.getState().renovarToken((await respuesta.json()) as LoginResponse);
    return true;
  } catch {
    // InteractionRequiredAuthError, red caída, etc. El 401 siguiente lleva al login.
    return false;
  }
}

/** Milisegundos hasta renovar un token que vence en <paramref name="expiraEn"/>. */
export function msHastaRenovar(expiraEn: Date, ahora: number = Date.now()): number {
  return Math.max(ESPERA_MINIMA_MS, expiraEn.getTime() - ahora - MARGEN_RENOVACION_MS);
}

function programar(expiraEn: Date | null): void {
  if (temporizador !== null) {
    clearTimeout(temporizador);
    temporizador = null;
  }
  if (expiraEn === null || authMode !== 'EntraId') return;
  temporizador = setTimeout(() => {
    temporizador = null;
    void renovarSesion();
  }, msHastaRenovar(expiraEn));
}

/**
 * Al volver a la pestaña (el navegador congela los temporizadores de las
 * pestañas en segundo plano) renueva de inmediato si ya está dentro del margen.
 */
function alVolverALaPestana(): void {
  if (document.visibilityState !== 'visible') return;
  const { expiresAt, accessToken } = useAuthStore.getState();
  if (accessToken && expiresAt && expiresAt.getTime() - Date.now() <= MARGEN_RENOVACION_MS) {
    void renovarSesion();
  }
}

/**
 * Programa la renovación cada vez que cambia el vencimiento del token (login,
 * cambio de empresa o renovación). Idempotente; devuelve la función para
 * detenerla (pruebas).
 */
export function iniciarRenovacionAutomatica(): () => void {
  if (dejarDeEscuchar) return dejarDeEscuchar;

  let ultimo = useAuthStore.getState().expiresAt;
  programar(ultimo);
  const desuscribir = useAuthStore.subscribe((estado) => {
    if (estado.expiresAt !== ultimo) {
      ultimo = estado.expiresAt;
      programar(ultimo);
    }
  });
  document.addEventListener('visibilitychange', alVolverALaPestana);

  dejarDeEscuchar = () => {
    desuscribir();
    document.removeEventListener('visibilitychange', alVolverALaPestana);
    programar(null);
    dejarDeEscuchar = null;
  };
  return dejarDeEscuchar;
}
