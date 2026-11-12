import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  CorreccionCartaPorte,
  CorregirCartaPorteBody,
  CrearTramoBody,
  DocumentoTerceroBody,
  TramoTraslado,
} from '@/features/facturacion/api/types';

const PEDIDOS = '/api/v1/facturacion/pedidos-facturables';
const CARTA_PORTE = '/api/v1/facturacion/carta-porte';

/** M-354 · <c>useTramosPedido(pedidoId)</c> — tramos del traslado con su modalidad. */
export function useTramosPedido(pedidoId: string | null | undefined, enabled = true) {
  return useQuery<TramoTraslado[]>({
    queryKey: pedidoId != null ? facturacionKeys.pedidoTramos(pedidoId) : ['facturacion', 'noop-tramos'],
    queryFn: async ({ signal }) => {
      if (pedidoId == null) throw new Error('useTramosPedido invocado sin id');
      const { data } = await apiRequest<TramoTraslado[]>(`${PEDIDOS}/${pedidoId}/tramos`, { signal });
      return data;
    },
    enabled: pedidoId != null && enabled,
  });
}

/** M-354 · agrega un tramo (vehículo propio o transportista tercero). Idempotency-Key. */
export function useCrearTramo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { pedidoId: string; body: CrearTramoBody; idempotencyKey: string }) => {
      const { data } = await apiRequest<TramoTraslado>(`${PEDIDOS}/${args.pedidoId}/tramos`, {
        method: 'POST',
        body: args.body,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: (_d, args) => {
      void queryClient.invalidateQueries({ queryKey: facturacionKeys.pedidoTramos(args.pedidoId) });
    },
  });
}

/** M-354 · registra el documento del transportista tercero (el tramo no emite Carta Porte). */
export function useRegistrarDocumentoTercero() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      pedidoId: string;
      tramoId: string;
      body: DocumentoTerceroBody;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<TramoTraslado>(
        `${PEDIDOS}/${args.pedidoId}/tramos/${args.tramoId}/documento-tercero`,
        { method: 'POST', body: args.body, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: (_d, args) => {
      void queryClient.invalidateQueries({ queryKey: facturacionKeys.pedidoTramos(args.pedidoId) });
    },
  });
}

/** M-354 · bitácora de correcciones de una Carta Porte. */
export function useCorreccionesCartaPorte(id: string | null | undefined) {
  return useQuery<CorreccionCartaPorte[]>({
    queryKey: id != null ? facturacionKeys.cartaPorteCorrecciones(id) : ['facturacion', 'noop-correcciones'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useCorreccionesCartaPorte invocado sin id');
      const { data } = await apiRequest<CorreccionCartaPorte[]>(`${CARTA_PORTE}/${id}/correcciones`, { signal });
      return data;
    },
    enabled: id != null,
  });
}

/** M-354 · corrige una Carta Porte observada (timbrado fallido) con motivo y rastro; luego se reintenta. */
export function useCorregirCartaPorte() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; body: CorregirCartaPorteBody; idempotencyKey: string }) => {
      const { data } = await apiRequest<CorreccionCartaPorte[]>(`${CARTA_PORTE}/${args.id}/corregir`, {
        method: 'POST',
        body: args.body,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: (_d, args) => {
      void queryClient.invalidateQueries({ queryKey: facturacionKeys.cartaPorteById(args.id) });
      void queryClient.invalidateQueries({ queryKey: facturacionKeys.cartaPorteCorrecciones(args.id) });
    },
  });
}
