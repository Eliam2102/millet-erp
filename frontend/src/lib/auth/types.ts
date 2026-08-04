/**
 * Tipos compartidos de auth — refleja los DTOs del API
 * (backend/src/Api/Auth/Models/*.cs). Si esos cambian, actualiza acá.
 *
 * En PR siguiente (ADR-0017 implementación) el codegen automático desde
 * OpenAPI reemplazará estos tipos manuales.
 */

export type AuthMode = 'EntraId' | 'FakeForLocalDev';

export interface UsuarioInfo {
  id: string;
  email: string;
  nombre: string;
}

/**
 * Configuración del módulo Compras por empresa. Viaja en
 * <c>LoginResponse</c> / <c>MeResponse</c> bajo el campo
 * <c>comprasSettings</c> para que el FE pueda condicionar UI sin un
 * fetch extra. Decisión 2026-05-13 (PR-A backend); ver
 * <c>compras.settings</c> en BD.
 *
 * - <c>autoGenerarOcAlAutorizar=true</c>: al autorizar una RQ con
 *   saldo de compra, el handler genera un OC borrador automático
 *   (narrativa A). Por consecuencia el comprador NO consolida desde
 *   el Sheet "Nueva OC" (las RQs ya están comprometidas).
 * - <c>autoGenerarOcAlAutorizar=false</c> (default): el comprador
 *   convierte manualmente desde la bandeja/detalle RQ ("Convertir
 *   a OC") o agrupa varias RQs en el Sheet "Nueva OC" (modo
 *   Consolidación N:1).
 */
export interface ComprasSettings {
  empresaId: string;
  autoGenerarOcAlAutorizar: boolean;
}

export interface EmpresaInfo {
  id: string;
  rfc: string;
  razonSocial: string;
  esLaActual: boolean;
}

export interface LoginResponse {
  accessToken: string;
  /** ISO8601 string con la expiración del token. */
  expiresAt: string;
  usuario: UsuarioInfo;
  empresas: EmpresaInfo[];
  /** Códigos de permiso (ej. "infra.health.leer") en la empresa actual. */
  permisos: string[];
  /** <c>null</c> si no hay empresa seleccionada en el JWT. */
  comprasSettings: ComprasSettings | null;
}

export interface MeResponse {
  userId: string;
  email: string;
  nombre: string;
  currentEmpresaId: string | null;
  departamentoId: string | null;
  permisos: string[];
  comprasSettings: ComprasSettings | null;
}

/**
 * Status del flujo de auth en el frontend:
 * - <c>idle</c>: estado inicial antes de cualquier intento
 * - <c>authenticating</c>: login en curso (silent acquire o popup)
 * - <c>authenticated</c>: token válido en memoria
 * - <c>unauthenticated</c>: sin sesión (post-logout o silent fallido)
 * - <c>error</c>: algún paso falló (mensaje en errorMessage)
 */
export type AuthStatus =
  | 'idle'
  | 'authenticating'
  | 'authenticated'
  | 'unauthenticated'
  | 'error';

export interface AuthState {
  status: AuthStatus;
  errorMessage: string | null;
  accessToken: string | null;
  expiresAt: Date | null;
  user: UsuarioInfo | null;
  empresas: EmpresaInfo[];
  currentEmpresaId: string | null;
  /** Códigos de permiso del usuario en la empresa actual. */
  permisos: string[];
  /** Settings del módulo Compras de la empresa actual; <c>null</c> si no hay empresa. */
  comprasSettings: ComprasSettings | null;
}

/** Usuario seed de dev para el DevUserSelector (ADR-0015). */
export interface SeedDevUser {
  oid: string;
  email: string;
  nombre: string;
  descripcion: string;
}
