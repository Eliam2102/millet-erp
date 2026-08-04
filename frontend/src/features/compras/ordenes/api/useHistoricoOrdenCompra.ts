import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * Mirror del DTO backend <c>EventoHistorico</c> (UF7-PR3, F7-PR3).
 */
export interface EventoHistorico {
  id: string;
  /** ISO 8601 UTC. */
  timestamp: string;
  /** "crear" / "actualizar" / "borrar" / etc. */
  operacion: string;
  /** Nombre de la entidad afectada (OrdenCompra, LineaOrdenCompra, etc.). */
  entidad: string;
  usuarioId: string | null;
  /** Resumen humano derivado del jsonb \`cambios\`. */
  resumen: string | null;
}

export interface ObtenerHistoricoOrdenCompraResponse {
  eventos: EventoHistorico[];
}

/**
 * <c>useHistoricoOrdenCompra(id)</c> — wrapper del endpoint
 * <c>GET /api/v1/compras/ordenes/{id}/historico</c> (UF7-PR3).
 * Lee <c>core.audit_log</c> filtrado por la OC y devuelve un timeline
 * cronológico ASC (más viejo arriba).
 *
 * <para>Cache moderado (1 min) — el histórico crece monótonamente
 * (nuevos eventos se agregan, los viejos no cambian) pero las
 * mutations sí lo invalidan via <c>ordenesKeys.detail()</c>.</para>
 */
export function useHistoricoOrdenCompra(id: string) {
  return useQuery({
    queryKey: [...ordenesKeys.detail(id), 'historico'] as const,
    enabled: !!id,
    staleTime: 60_000,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ObtenerHistoricoOrdenCompraResponse>(
        `/api/v1/compras/ordenes/${id}/historico`,
        { signal },
      );
      return data;
    },
  });
}
