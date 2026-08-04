import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type {
  AlertaCarteraResponse,
  PagedResponse,
} from '@/features/cxc/api/types';

const BASE = '/api/v1/cuentas-por-cobrar/alertas';

export interface ListarAlertasFiltros {
  atendida?: boolean;
  clienteId?: string;
  tipo?: number;
  offset?: number;
  limit?: number;
}

/**
 * <c>useAlertasCartera(filtros)</c> — bandeja de alertas generadas por
 * el worker diario (CXC-PR8). Lectura con <c>cartera.leer</c>;
 * <c>opts.enabled</c> permite gatearlo desde el landing (sin permiso no
 * se dispara).
 */
export function useAlertasCartera(
  filtros: ListarAlertasFiltros = {},
  opts: { enabled?: boolean } = {},
) {
  return useQuery<PagedResponse<AlertaCarteraResponse>>({
    queryKey: cxcKeys.alertasList({
      atendida: filtros.atendida ?? null,
      clienteId: filtros.clienteId ?? null,
      tipo: filtros.tipo ?? null,
      offset: filtros.offset ?? 0,
      limit: filtros.limit ?? 200,
    }),
    enabled: opts.enabled ?? true,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.atendida != null)
        params.set('atendida', String(filtros.atendida));
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.tipo != null) params.set('tipo', String(filtros.tipo));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<AlertaCarteraResponse>>(
        qs ? `${BASE}?${qs}` : BASE,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useAtenderAlerta()</c> — atender es una gestión de cartera (gate
 * <c>cobranza.registrar</c>); X-Expected-Version + Idempotency-Key.
 */
export function useAtenderAlerta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AlertaCarteraResponse>(
        `${BASE}/${args.id}/atender`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.alertas() });
    },
  });
}
