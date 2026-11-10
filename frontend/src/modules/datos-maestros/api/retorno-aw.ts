import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** M-160: avisos a A+W (factura, pago, estatus) que esperan a que A+W vuelva. */
export interface ResumenRetornoAw {
  pendientes: number;
  detenidos: number;
  masAntiguoEnUtc: string | null;
  masAntiguoTipoEvento: string | null;
  masAntiguoIntentos: number | null;
  masAntiguoProximoIntentoUtc: string | null;
  masAntiguoUltimoError: string | null;
  alerta: boolean;
}
export interface ReprocesoDeadLetterRetornoAw {
  leidos: number;
  aplicados: number;
  pendientes: number;
  invalidos: number;
  fallidos: number;
}

const key = ['integraciones-aw', 'retorno', 'pendientes'] as const;
const base = '/api/v1/integraciones/aw/retorno';

export function useResumenRetornoAw(enabled: boolean) {
  return useQuery({
    queryKey: key,
    queryFn: async ({ signal }) =>
      (await apiRequest<ResumenRetornoAw>(`${base}/pendientes/resumen`, { signal })).data,
    enabled,
    refetchInterval: 60_000,
  });
}

export function useReprocesarDeadLetterRetornoAw() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      (await apiRequest<ReprocesoDeadLetterRetornoAw>(`${base}/dead-letter/reprocesar`, {
        method: 'POST', idempotencyKey: crypto.randomUUID(),
      })).data,
    onSettled: () => queryClient.invalidateQueries({ queryKey: key }),
  });
}
