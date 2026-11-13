import { useMutation, useQueries, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { RedondeoPagoResponse } from './types';
import { normalizarRedondeos } from '../lib/redondeo-pago';

const BASE = '/api/v1/tesoreria/pagos/redondeo';
const KEY = ['tesoreria', 'redondeo-pago'] as const;

/** M-326 (b): solicitudes de redondeo de cada factura (GET con `facturaId`); se refresca mientras haya una esperando. */
export function useRedondeosDeFacturas(facturaIds: readonly string[]) {
  const resultados = useQueries({
    queries: facturaIds.map((id) => ({
      queryKey: [...KEY, 'factura', id],
      queryFn: async ({ signal }: { signal: AbortSignal }) =>
        normalizarRedondeos((await apiRequest<unknown>(`${BASE}?facturaId=${id}`, { signal })).data),
      refetchInterval: (q: { state: { data?: RedondeoPagoResponse[] } }) =>
        q.state.data?.some((s) => s.estado === 'Solicitada') ? 10_000 : false,
    })),
  });
  const porFactura: Record<string, RedondeoPagoResponse[]> = {};
  facturaIds.forEach((id, i) => {
    porFactura[id] = resultados[i]?.data ?? [];
  });
  return porFactura;
}

export function useSolicitarRedondeoPago() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { facturaProveedorId: string; redondeo: number; motivo: string; idempotencyKey: string }) => {
      const { idempotencyKey, ...body } = a;
      return (await apiRequest<RedondeoPagoResponse>(BASE, { method: 'POST', body, idempotencyKey })).data;
    },
    onSuccess: () => void qc.invalidateQueries({ queryKey: KEY }),
  });
}

export function useAutorizarRedondeoPago() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { id: string; idempotencyKey: string }) =>
      (await apiRequest<RedondeoPagoResponse>(`${BASE}/${a.id}/autorizar`, { method: 'POST', idempotencyKey: a.idempotencyKey })).data,
    onSuccess: () => void qc.invalidateQueries({ queryKey: KEY }),
  });
}

export function useRechazarRedondeoPago() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { id: string; motivo: string; idempotencyKey: string }) =>
      (
        await apiRequest<RedondeoPagoResponse>(`${BASE}/${a.id}/rechazar`, {
          method: 'POST',
          body: { motivo: a.motivo },
          idempotencyKey: a.idempotencyKey,
        })
      ).data,
    onSuccess: () => void qc.invalidateQueries({ queryKey: KEY }),
  });
}
