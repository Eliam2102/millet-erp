import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type {
  SettingsSchemaResponse,
} from '@/lib/admin/types';
import type { AdminModulo } from '@/lib/admin/registry';

/**
 * Llave de cache canónica del schema de settings de un módulo. Se expone
 * para invalidaciones manuales (por ejemplo tras un PATCH exitoso desde
 * <c>&lt;AutoSettingsForm/&gt;</c>).
 */
export function settingsSchemaQueryKey(modulo: AdminModulo) {
  return ['admin', 'settings-schema', modulo] as const;
}

/**
 * Hook de TanStack Query para
 * <c>GET /api/v1/{modulo}/settings/schema</c>. El backend ya filtra los
 * items por <c>permisoLeer</c> contra el usuario actual, así que el
 * componente solo necesita renderizar lo que recibe.
 *
 * <para>Si el módulo no tiene un <c>ISettingsSchemaProvider</c>
 * registrado en DI, el endpoint devuelve <c>404</c> y el query queda en
 * estado <c>error</c> — la UI muestra mensaje neutro.</para>
 */
export function useSettingsSchema(modulo: AdminModulo) {
  return useQuery({
    queryKey: settingsSchemaQueryKey(modulo),
    queryFn: async () => {
      const { data } = await apiRequest<SettingsSchemaResponse>(
        `/api/v1/${modulo}/settings/schema`,
      );
      return data;
    },
  });
}
