/// <reference types="vite/client" />

/**
 * Declaración de tipos para las variables de entorno expuestas a Vite. Vite
 * inyecta estas en `import.meta.env` y las inlinea en build time, así que el
 * compilador puede tree-shake código condicional según el modo (ej. el
 * DevUserSelector solo se incluye si `VITE_AUTH_MODE === 'FakeForLocalDev'`).
 */
interface ImportMetaEnv {
  /** Modo de autenticación. Default 'FakeForLocalDev' en dev local. */
  readonly VITE_AUTH_MODE: 'EntraId' | 'FakeForLocalDev';

  /** Base URL del API. Vacío usa origen actual (mismo host, ideal para prod). */
  readonly VITE_API_BASE_URL: string;

  /** Tenant ID de Entra ID (GUID). Solo requerido en EntraId mode. */
  readonly VITE_ENTRA_TENANT_ID: string;

  /**
   * Client ID de la SPA app registration en Entra (NO el del API).
   * Solo requerido en EntraId mode.
   */
  readonly VITE_ENTRA_CLIENT_ID: string;

  /**
   * Audience del API. Formato típico: `api://<api-client-id>/access_as_user`.
   * MSAL pide tokens con esta scope; el API los valida con el mismo valor.
   */
  readonly VITE_API_AUDIENCE: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
