import { type Configuration, PublicClientApplication } from '@azure/msal-browser';
import type { AuthMode, SeedDevUser } from '@/lib/auth/types';

/**
 * Modo activo del frontend. Vite inlinea import.meta.env en build, así que
 * el compilador puede tree-shake ramas que no apliquen al modo actual
 * (ej. el DevUserSelector se elimina del bundle de prod).
 */
export const authMode: AuthMode = (import.meta.env.VITE_AUTH_MODE ??
  'FakeForLocalDev') as AuthMode;

/**
 * Base URL del API. Vacío significa "usa el origen actual" (mismo host:
 * el deploy típico en producción mete frontend y backend detrás del mismo
 * App Service / Front Door).
 */
export const apiBaseUrl: string = (
  import.meta.env.VITE_API_BASE_URL ?? ''
).replace(/\/$/, '');

/**
 * Construye la instancia de MSAL si el modo es EntraId. Devuelve null en
 * modo FakeForLocalDev — el flujo de dev no toca Entra.
 */
export function buildMsalInstance(): PublicClientApplication | null {
  if (authMode !== 'EntraId') {
    return null;
  }

  const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID;
  const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID;

  if (!tenantId || !clientId) {
    throw new Error(
      'VITE_ENTRA_TENANT_ID y VITE_ENTRA_CLIENT_ID son requeridos en modo EntraId.',
    );
  }

  const config: Configuration = {
    auth: {
      clientId,
      authority: `https://login.microsoftonline.com/${tenantId}`,
      redirectUri: window.location.origin + '/',
      postLogoutRedirectUri: window.location.origin + '/',
    },
    cache: {
      // sessionStorage: persiste cuenta + cache de Entra dentro de la pestaña.
      // localStorage tiene más exposición a XSS; sessionStorage es el default
      // recomendado por Microsoft para SPAs sensibles.
      cacheLocation: 'sessionStorage',
    },
  };

  return new PublicClientApplication(config);
}

/**
 * Scopes que el frontend pide a MSAL al hacer login. La audience se
 * configura via VITE_API_AUDIENCE (formato `api://<api-client-id>/access_as_user`).
 * El JWT resultante tiene como audience el client ID del API; el
 * EntraTokenValidator del backend lo verifica contra Auth:EntraId:Audience.
 */
export const apiScopes: string[] = [
  import.meta.env.VITE_API_AUDIENCE || 'openid',
];

/**
 * Usuarios seed disponibles en el DevUserSelector (modo FakeForLocalDev).
 * Solo `dev-superadmin` está bootstrappeado con rol y empresa por el
 * BootstrapSuperAdminHostedService; los demás se auto-provisionan en el
 * primer login (sin asignaciones, útil para probar UI de "usuario sin
 * empresa"). Ver ADR-0015.
 */
export const seedDevUsers: SeedDevUser[] = [
  {
    oid: 'dev-superadmin',
    email: 'superadmin@dev.local',
    nombre: 'Super Admin (Dev)',
    descripcion: 'Acceso total — el bootstrap le asigna todos los permisos.',
  },
  {
    oid: 'dev-facturador',
    email: 'facturador@dev.local',
    nombre: 'Facturador Dev',
    descripcion: 'Sin asignaciones iniciales — útil para probar UI de no-acceso.',
  },
  {
    oid: 'dev-cobrador',
    email: 'cobrador@dev.local',
    nombre: 'Cobrador Dev',
    descripcion: 'Sin asignaciones iniciales.',
  },
  {
    oid: 'dev-auditor',
    email: 'auditor@dev.local',
    nombre: 'Auditor Dev',
    descripcion: 'Sin asignaciones iniciales.',
  },
];
