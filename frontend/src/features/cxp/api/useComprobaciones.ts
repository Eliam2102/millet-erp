import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  cxpKeys,
  type ListarComprobacionesFiltros,
} from '@/features/cxp/api/keys';
import type {
  ComprobacionGastosDetalle,
  ComprobacionGastosListItem,
  CrearComprobacionAduanalesCommand,
  CrearComprobacionAduanalesResponse,
  CrearComprobacionCajaChicaCommand,
  CrearComprobacionCajaChicaResponse,
  PagedResponse,
  RechazarComprobacionCommand,
  TransicionComprobacionResponse,
} from '@/features/cxp/api/types';

/**
 * Hooks de Comprobaciones de Gastos (FE-F4-PR2). Cubre las 2
 * variantes implementadas en backend (Caja Chica + Aduanales) más las
 * transiciones comunes (enviar-revisión, autorizar, aplicar, rechazar)
 * y la doble firma de Aduanales (autorizar-nivel1 + autorizar-nivel2).
 *
 * <para>Viáticos y TC entran en FE-F5-PR1 y FE-F6-PR1 respectivamente.</para>
 */

function buildQuery(filtros: object): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filtros)) {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, String(value));
    }
  }
  return params.toString();
}

export function useComprobaciones(
  filtros: ListarComprobacionesFiltros = {},
) {
  return useQuery({
    queryKey: cxpKeys.comprobacionesList(filtros),
    queryFn: async ({ signal }) => {
      const q = buildQuery(filtros);
      const path = q
        ? `/api/v1/cuentas-por-pagar/comprobaciones?${q}`
        : '/api/v1/cuentas-por-pagar/comprobaciones';
      const { data } = await apiRequest<
        PagedResponse<ComprobacionGastosListItem>
      >(path, { signal });
      return data;
    },
  });
}

export function useComprobacionGastos(id: string | undefined) {
  return useQuery({
    queryKey: id
      ? cxpKeys.comprobacionById(id)
      : ['cxp', 'comprobacion', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ComprobacionGastosDetalle>(
        `/api/v1/cuentas-por-pagar/comprobaciones/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useCrearComprobacionCajaChica() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearComprobacionCajaChicaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CrearComprobacionCajaChicaResponse>(
        '/api/v1/cuentas-por-pagar/comprobaciones/caja-chica',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.comprobaciones() });
    },
  });
}

export function useCrearComprobacionAduanales() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearComprobacionAduanalesCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CrearComprobacionAduanalesResponse>(
        '/api/v1/cuentas-por-pagar/comprobaciones/aduanales',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.comprobaciones() });
    },
  });
}

function transitionMutation(suffix: string) {
  return {
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<TransicionComprobacionResponse>(
        `/api/v1/cuentas-por-pagar/comprobaciones/${args.id}/${suffix}`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
  };
}

export function useEnviarComprobacionARevision() {
  const queryClient = useQueryClient();
  return useMutation({
    ...transitionMutation('enviar-revision'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.comprobaciones() });
    },
  });
}

export function useAutorizarComprobacion() {
  const queryClient = useQueryClient();
  return useMutation({
    ...transitionMutation('autorizar'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.comprobaciones() });
    },
  });
}

export function useAplicarComprobacion() {
  const queryClient = useQueryClient();
  return useMutation({
    ...transitionMutation('aplicar'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.comprobaciones() });
    },
  });
}

export function useAutorizarNivel1Aduanales() {
  const queryClient = useQueryClient();
  return useMutation({
    ...transitionMutation('autorizar-nivel1'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.comprobaciones() });
    },
  });
}

export function useAutorizarNivel2Aduanales() {
  const queryClient = useQueryClient();
  return useMutation({
    ...transitionMutation('autorizar-nivel2'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.comprobaciones() });
    },
  });
}

export function useRechazarComprobacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: RechazarComprobacionCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<TransicionComprobacionResponse>(
        `/api/v1/cuentas-por-pagar/comprobaciones/${args.id}/rechazar`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.comprobaciones() });
    },
  });
}
