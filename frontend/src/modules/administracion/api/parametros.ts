import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ActualizarParametroPayload,
  ListarParametrosResponse,
  ParametroResponse,
} from '@/modules/administracion/api/types';

/**
 * Hooks de TanStack Query del recurso Parámetros globales
 * (F-Admin-PR7.1 backend).
 *
 * <para>Endpoints consumidos:</para>
 * <list>
 *   <item><c>GET   /api/v1/admin/parametros[?modulo=]</c> — lista sin
 *         paginación; <c>modulo=""</c> filtra a globales del sistema
 *         (Modulo IS NULL); <c>modulo=&lt;nombre&gt;</c> filtra al
 *         módulo; ausente = todos.</item>
 *   <item><c>PATCH /api/v1/admin/parametros/{clave}</c> — actualiza
 *         valor (Idempotency-Key requerido). 404 si la clave no existe;
 *         422 si el valor no parsea según el tipo declarado.</item>
 * </list>
 */

const STALE = 30_000;

// ─── Queries ────────────────────────────────────────────────────────

/**
 * Lista parámetros globales. Pasar <c>modulo</c>:
 * <list>
 *   <item><c>undefined</c> → todos los parámetros (sistema + módulos).</item>
 *   <item><c>""</c> → solo parámetros globales del sistema.</item>
 *   <item><c>"compras"</c> → parámetros del módulo Compras.</item>
 * </list>
 */
export function useParametros(modulo?: string | null) {
  return useQuery({
    queryKey: adminKeys.parametrosList(modulo),
    queryFn: async ({ signal }) => {
      const path =
        modulo != null
          ? `/api/v1/admin/parametros?modulo=${encodeURIComponent(modulo)}`
          : '/api/v1/admin/parametros';
      const { data } = await apiRequest<ListarParametrosResponse>(path, {
        signal,
      });
      return data;
    },
    staleTime: STALE,
  });
}

// ─── Mutations ──────────────────────────────────────────────────────

export interface ActualizarParametroArgs {
  clave: string;
  payload: ActualizarParametroPayload;
  idempotencyKey: string;
}

export function useActualizarParametro() {
  const queryClient = useQueryClient();
  return useMutation<ParametroResponse, Error, ActualizarParametroArgs>({
    mutationFn: async ({ clave, payload, idempotencyKey }) => {
      const { data } = await apiRequest<ParametroResponse>(
        `/api/v1/admin/parametros/${encodeURIComponent(clave)}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminKeys.parametros() });
    },
  });
}
