import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, useBodyScopedIdempotencyKey } from '@/lib/api';
import { ptKeys } from './hooks';

const BASE = '/api/v1/producto-terminado/liquidaciones';

/** Gemelos de los DTOs de liquidación del chofer en caja (PT5.7). */
export type EstadoLiquidacion = 'Pendiente' | 'ConDiferencia' | 'Cerrada';

export interface LiquidacionResumenDto {
  salidaId: string;
  folio: string;
  sucursalId: string;
  destino: string;
  emitidaAt: string;
  pedidos: number;
  contraEntrega: number;
  estado: EstadoLiquidacion;
  diferencia: number;
}

export interface LiquidacionLineaDto {
  albaranId: string;
  albaranFolio: string;
  numeroPedido: string;
  cliente: string;
  contraEntrega: boolean;
  esperado: number;
  firmado: boolean;
  efectivo: number;
  voucher: number;
  voucherReferencia: string | null;
  diferencia: number;
  conciliado: boolean;
  avisado: boolean;
}

export interface ImprevistoDto {
  descripcion: string;
  importe: number;
}

export interface LiquidacionDto {
  salidaId: string;
  folio: string;
  destino: string;
  estado: EstadoLiquidacion;
  liquidadaAt: string | null;
  albaranes: LiquidacionLineaDto[];
  imprevistos: ImprevistoDto[];
  totalEsperado: number;
  totalCobrado: number;
  diferencia: number;
}

export interface LiquidarBody {
  albaranes: { albaranId: string; firmado: boolean; efectivo: number; voucher: number; voucherReferencia: string | null }[];
  imprevistos: ImprevistoDto[];
}

export function useLiquidaciones() {
  return useQuery({
    queryKey: [...ptKeys.all, 'liquidaciones'],
    queryFn: async ({ signal }) =>
      (await apiRequest<{ items: LiquidacionResumenDto[]; total: number }>(`${BASE}?pageSize=100`, { signal })).data,
  });
}

export function useLiquidacion(salidaId: string | null) {
  return useQuery({
    queryKey: [...ptKeys.all, 'liquidacion', salidaId],
    enabled: !!salidaId,
    queryFn: async ({ signal }) => (await apiRequest<LiquidacionDto>(`${BASE}/${salidaId}`, { signal })).data,
  });
}

export function useLiquidarRuta() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: ({ salidaId, body }: { salidaId: string; body: LiquidarBody }) =>
      apiRequest<LiquidacionDto>(`${BASE}/${salidaId}`, { method: 'POST', body, idempotencyKey: keyFor({ salidaId, body }) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}

/** Avisa a CxC de la contra entrega del día que sigue sin liquidar (lo mismo que hace el cierre automático). */
export function useCierreDiaContraEntrega() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: (instante: string) =>
      apiRequest<{ avisos: number }>(`${BASE}/cierre-dia`, { method: 'POST', idempotencyKey: keyFor({ instante }) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ptKeys.all }),
  });
}
