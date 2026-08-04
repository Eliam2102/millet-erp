import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { identidadKeys } from '@/modules/identidad/api/keys';
import type { ListarPermisosResponse } from '@/modules/identidad/api/types';

/**
 * Hook del catálogo de permisos canónicos (F-Admin-PR3.3 backend).
 *
 * <para>Endpoints consumidos:</para>
 * <list>
 *   <item><c>GET /api/v1/identidad/permisos?agrupado=true|false</c>:
 *     cuando <c>agrupado=true</c> el backend puebla <c>grupos</c>
 *     (lista por módulo). Cuando <c>agrupado=false</c>, solo <c>items</c>
 *     lineal.</item>
 * </list>
 *
 * <para>El catálogo es prácticamente estático (cambia solo cuando se
 * agrega un permiso nuevo al deploy), por eso se cachea con
 * <c>staleTime: 5 min</c>.</para>
 */
export function usePermisos(agrupado = false) {
  return useQuery({
    queryKey: identidadKeys.permisosList(agrupado),
    queryFn: async ({ signal }) => {
      const path = agrupado
        ? '/api/v1/identidad/permisos?agrupado=true'
        : '/api/v1/identidad/permisos';
      const { data } = await apiRequest<ListarPermisosResponse>(path, {
        signal,
      });
      return data;
    },
    staleTime: 5 * 60 * 1000,
  });
}
