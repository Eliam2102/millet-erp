import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** Fila de la bandeja de NC (backend `NotaCreditoBandejaItem`); 86e3wjt5r agrega folio A+W, factura y fecha. */
export interface NotaCreditoPorAutorizar {
  id: string;
  folio: string;
  estado: string;
  receptorNombre: string;
  receptorRfc: string;
  motivo: 'Bonificacion' | 'Devolucion' | string;
  total: number;
  moneda: string;
  folioAw: string | null;
  facturaFolio: string | null;
  fechaRecibida: string | null;
  vendedorAw?: string | null;
  /** M-087: arriba del umbral (35,000 MXN por omisión) firma también Crédito y Cobranza. */
  requiereFirmaCredito?: boolean;
  firmadaGerente?: boolean;
  firmadaCredito?: boolean;
}
export interface AutorizarNotaCreditoResultado {
  id: string;
  estado: string;
  uuid: string | null;
  folio: string;
  total: number;
  requiereFirmaCredito?: boolean;
  firmadaGerente?: boolean;
  firmadaCredito?: boolean;
  /** M-087: firma que falta para timbrar ('CreditoYCobranza' | 'Gerente'); null si ya se timbró. */
  pendiente?: string | null;
}
const BASE = '/api/v1/facturacion/notas-credito';
const KEY = ['facturacion', 'notas-credito-por-autorizar'] as const;

export const TAMANO_PAGINA_NC_POR_AUTORIZAR = 10;

export function useNotasCreditoPorAutorizar(offset = 0, limit = TAMANO_PAGINA_NC_POR_AUTORIZAR) {
  return useQuery({
    queryKey: [...KEY, offset, limit],
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<{ items: NotaCreditoPorAutorizar[]; total?: number }>(
          `${BASE}?estado=PorAutorizar&limit=${limit}&offset=${offset}`,
          { signal },
        )
      ).data,
  });
}

export function useAutorizarNotaCredito() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, idempotencyKey }: { id: string; idempotencyKey: string }) =>
      (
        await apiRequest<AutorizarNotaCreditoResultado>(`${BASE}/${id}/autorizar`, {
          method: 'POST',
          idempotencyKey,
        })
      ).data,
    onSettled: () => client.invalidateQueries({ queryKey: ['facturacion'] }),
  });
}

/** M-087: firma de Crédito y Cobranza; si el gerente ya firmó, la nota se timbra. */
export function useFirmarCreditoNotaCredito() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, idempotencyKey }: { id: string; idempotencyKey: string }) =>
      (
        await apiRequest<AutorizarNotaCreditoResultado>(`${BASE}/${id}/autorizar-credito`, {
          method: 'POST',
          idempotencyKey,
        })
      ).data,
    onSettled: () => client.invalidateQueries({ queryKey: ['facturacion'] }),
  });
}
