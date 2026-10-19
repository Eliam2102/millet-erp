import { useQuery } from '@tanstack/react-query';
import { apiFetchJson } from '@/lib/auth/api-client';
import type { MeResponse } from '@/lib/auth/types';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * Empleado ligado al usuario actual (<c>GET /api/auth/me</c> → <c>empleadoId</c>).
 * <c>tieneEmpleado</c> es <c>null</c> mientras carga o si /me falla: en ese caso la UI
 * no bloquea y el backend sigue siendo quien valida (<c>VIA_USUARIO_SIN_EMPLEADO</c>).
 */
export function useMiEmpleado(): { tieneEmpleado: boolean | null } {
  const userId = useAuthStore((s) => s.user?.id);
  const query = useQuery({
    queryKey: ['auth', 'me', 'empleado', userId],
    enabled: !!userId,
    staleTime: 5 * 60_000,
    queryFn: () => apiFetchJson<MeResponse>('/api/auth/me'),
  });
  if (!query.data) return { tieneEmpleado: null };
  return { tieneEmpleado: query.data.empleadoId != null };
}
