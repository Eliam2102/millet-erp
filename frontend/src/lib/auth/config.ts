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
      // Redirige al origin ('/') para coincidir exactamente con la Redirect URI registrada en Entra ID.
      // El route guard de TanStack Router (_app.tsx) intercepta la llegada sin sesión y redirige a /login.
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

function resolverApiScope(audienceRaw?: string): string {
  if (!audienceRaw || audienceRaw.trim() === '') return 'openid';
  const raw = audienceRaw.trim();
  if (raw === 'openid') return 'openid';
  if (raw.includes('/', raw.startsWith('api://') ? 7 : 0)) {
    return raw;
  }
  const base = raw.startsWith('api://') ? raw : `api://${raw}`;
  return `${base.replace(/\/$/, '')}/access_as_user`;
}

export const apiScopes: string[] = [
  resolverApiScope(import.meta.env.VITE_API_AUDIENCE),
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
    oid: '6a851ba1-7d19-4132-8d2a-8e1fb152df78',
    email: 'uzieltzab@outlook.com',
    nombre: 'Uziel Alejandro Tzab Puc (Admin)',
    descripcion: 'SuperAdmin en Microsoft Entra ID con acceso total.',
  },
  {
    oid: '073fb6fb-c4c6-44be-a37a-8a5277fe37af',
    email: 'uziel.test@uzieltzaboutlook.onmicrosoft.com',
    nombre: 'Uziel Test (Azure Sandbox)',
    descripcion: 'Usuario del Sandbox de Microsoft Entra ID (miembro nuevo sin asignaciones iniciales).',
  },
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
