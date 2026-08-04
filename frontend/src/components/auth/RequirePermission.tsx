import type { ReactNode } from 'react';
import { useHasPermission } from '@/lib/auth/useHasPermission';

interface RequirePermissionProps {
  /** Código del permiso requerido (ej. <c>"identidad.usuarios.crear"</c>). */
  code: string;
  /** Contenido a mostrar si el usuario tiene el permiso. */
  children: ReactNode;
  /**
   * Contenido alternativo si el usuario NO tiene el permiso. Default null
   * (oculta sin avisar). Para UX más explícita, pasar un mensaje o un
   * botón "Solicitar acceso".
   */
  fallback?: ReactNode;
}

/**
 * Wrapper declarativo: renderiza <c>children</c> solo si el usuario tiene
 * el permiso. Para rendering condicional dentro de un componente, usar
 * <see cref="useHasPermission" /> directamente.
 *
 * @example
 * ```tsx
 * <RequirePermission code={PermisosCanonicos.InfraAuditLogLeer}>
 *   <AuditLogViewer />
 * </RequirePermission>
 * ```
 */
export function RequirePermission({
  code,
  children,
  fallback = null,
}: RequirePermissionProps) {
  const hasPermission = useHasPermission(code);
  return <>{hasPermission ? children : fallback}</>;
}
