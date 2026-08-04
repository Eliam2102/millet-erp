import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type {
  ConfiguracionReposicionResponse,
  DestinoReposicionCaja,
  EmitirReposicionManualResponse,
  PagedResponse,
  ReposicionCajaListItem,
  SaldoPendienteReposicion,
} from './types';

// GI-PR4b (doc 12 §D2/Q4): reposiciones de caja chica — saldos por
// reponer, bandeja de reposiciones emitidas, corte manual y mínimo por
// sucursal.

const BASE = '/api/v1/cuentas-por-pagar/reposiciones-caja';

export const reposicionesKeys = {
  root: ['cxp', 'reposiciones'] as const,
  saldos: () => [...reposicionesKeys.root, 'saldos'] as const,
  lista: (sucursalId?: string) =>
    [...reposicionesKeys.root, 'lista', sucursalId ?? 'todas'] as const,
};

export function useSaldosReposicion() {
  return useQuery({
    queryKey: reposicionesKeys.saldos(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<SaldoPendienteReposicion[]>(
        `${BASE}/saldos`,
        { signal },
      );
      return data;
    },
  });
}

export function useReposiciones(sucursalId?: string) {
  const params = new URLSearchParams({ limit: '100' });
  if (sucursalId) params.set('sucursalId', sucursalId);
  return useQuery({
    queryKey: reposicionesKeys.lista(sucursalId),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<PagedResponse<ReposicionCajaListItem>>(
        `${BASE}/?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
}

export function useEmitirReposicionManual() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      sucursalId: string;
      destino: DestinoReposicionCaja;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EmitirReposicionManualResponse>(
        `${BASE}/emitir`,
        {
          method: 'POST',
          body: { sucursalId: args.sucursalId, destino: args.destino },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: reposicionesKeys.root });
    },
  });
}

export function useConfigurarReposicion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      sucursalId: string;
      montoMinimo: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ConfiguracionReposicionResponse>(
        `${BASE}/configuracion`,
        {
          method: 'PUT',
          body: { sucursalId: args.sucursalId, montoMinimo: args.montoMinimo },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: reposicionesKeys.root });
    },
  });
}
