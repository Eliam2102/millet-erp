import { useQuery } from '@tanstack/react-query';
import { apiQueryRequest } from '@/lib/api/query-request';
import { useAuthStore } from '@/lib/auth/auth-store';

/** Sucursal a la que el usuario tiene acceso (<c>GET /api/auth/sucursales</c>). */
export interface SucursalPermitida {
  id: string;
  clave: string;
  nombre: string;
}

/**
 * <c>useSucursalesPermitidas()</c> — sucursales de la sesión actual. Misma
 * query key que el selector de la topbar, así comparten caché.
 */
export function useSucursalesPermitidas() {
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  return useQuery({
    queryKey: ['auth', 'sucursales', empresaId],
    enabled: empresaId != null,
    queryFn: async (context) =>
      (await apiQueryRequest<SucursalPermitida[]>('/api/auth/sucursales', context)).data,
    staleTime: 30_000,
  });
}

/**
 * Default de sucursal destino: con exactamente una sucursal permitida, esa;
 * con varias (o ninguna) devuelve <c>null</c> y el usuario debe elegir.
 */
export function sucursalPorDefecto(
  permitidas: readonly SucursalPermitida[],
): string | null {
  return permitidas.length === 1 ? permitidas[0].id : null;
}
