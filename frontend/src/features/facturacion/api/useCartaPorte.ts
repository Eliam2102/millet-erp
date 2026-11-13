import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  BandejaPaginada,
  CartaPorteBandejaItem,
  CartaPorteDetalleResponse,
  EmitirCartaPorteCommand,
  EmitirCartaPorteResponse,
  SiguienteTramoCommand,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/carta-porte';

/** <c>useListarCartaPorte(estado)</c> — bandeja de Carta Portes (B11). */
export function useListarCartaPorte(
  estado: number | undefined,
  pagina: { q?: string; offset?: number; limit?: number } = {},
) {
  return useQuery<BandejaPaginada<CartaPorteBandejaItem>>({
    queryKey: facturacionKeys.cartaPorteList({ estado: estado ?? null, ...pagina }),
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (estado != null) params.set('estado', String(estado));
      if (pagina.q) params.set('q', pagina.q);
      if (pagina.offset != null) params.set('offset', String(pagina.offset));
      if (pagina.limit != null) params.set('limit', String(pagina.limit));
      const qs = params.toString();
      const { data } = await apiRequest<BandejaPaginada<CartaPorteBandejaItem>>(
        qs ? `${BASE}?${qs}` : `${BASE}/`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useCartaPorte(id)</c> — detalle (vehículo, operador, mercancías) (B11). */
export function useCartaPorte(id: string | null | undefined) {
  return useQuery<CartaPorteDetalleResponse>({
    queryKey:
      id != null ? facturacionKeys.cartaPorteById(id) : ['facturacion', 'noop-cp'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useCartaPorte invocado sin id');
      const { data } = await apiRequest<CartaPorteDetalleResponse>(
        `${BASE}/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/** <c>useEmitirCartaPorte()</c> — emite una Carta Porte 3.1 (Idempotency-Key). */
export function useEmitirCartaPorte() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: EmitirCartaPorteCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EmitirCartaPorteResponse>(`${BASE}/`, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.cartaPorte() });
    },
  });
}

/**
 * <c>useSiguienteTramo()</c> — crea el siguiente tramo de una Carta Porte
 * existente (hereda receptor/emisor; referencia el tramo previo).
 */
export function useSiguienteTramo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      previaId: string;
      command: SiguienteTramoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EmitirCartaPorteResponse>(
        `${BASE}/${args.previaId}/siguiente-tramo`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.cartaPorte() });
    },
  });
}
