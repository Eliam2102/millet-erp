import { useAuthStore } from '@/lib/auth/auth-store';
import type { ComprasSettings } from '@/lib/auth/types';

/**
 * Devuelve los settings del módulo Compras de la empresa actual. El payload
 * viaja en <c>LoginResponse</c> / <c>MeResponse</c> y vive en el
 * <c>useAuthStore</c> (decisión Q2-a del PR-A backend: settings en la
 * sesión, sin fetch extra).
 *
 * <para>
 * Devuelve <c>null</c> mientras no haya sesión autenticada con empresa
 * seleccionada. Los consumidores deben tratar <c>null</c> como "aún no
 * sé" — comportamiento conservador es ocultar el botón que dependa del
 * setting hasta que cargue.
 * </para>
 */
export function useComprasSettings(): ComprasSettings | null {
  return useAuthStore((s) => s.comprasSettings);
}

/**
 * Conveniencia: ¿está activo el modo automático
 * (<c>AutoGenerarOcAlAutorizar=true</c>)? Si el setting no está cargado
 * todavía, devuelve <c>null</c> — el componente decide si ocultar o
 * mostrar mientras tanto.
 */
export function useAutoGenerarOcAlAutorizar(): boolean | null {
  const settings = useComprasSettings();
  return settings?.autoGenerarOcAlAutorizar ?? null;
}
