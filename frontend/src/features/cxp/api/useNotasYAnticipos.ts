import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  cxpKeys,
  type ListarAnticiposFiltros,
  type ListarNotasCargoFiltros,
  type ListarNotasCreditoFiltros,
} from '@/features/cxp/api/keys';
import type {
  AplicarAnticipoAFacturaCommand,
  AplicarNotaCreditoAFacturaCommand,
  AnticipoListItem,
  CapturarAnticipoCommand,
  CapturarAnticipoResponse,
  CapturarNotaCreditoCommand,
  CapturarNotaCreditoResponse,
  CrearNotaCargoCommand,
  CrearNotaCargoResponse,
  NotaCargoDetalle,
  NotaCargoListItem,
  NotaCreditoDetalle,
  NotaCreditoListItem,
  PagedResponse,
  VincularFacturaNotaCreditoCommand,
  VincularFacturaNotaCreditoResponse,
} from '@/features/cxp/api/types';

/**
 * Hooks de Notas de Crédito + Anticipos + Notas de Cargo (FE-F4-PR1).
 * Cada recurso tiene su bandeja paginada y mutación de captura. La
 * aplicación a saldo de factura usa endpoints de
 * <c>FacturasEndpoints</c>; aquí los exponemos como hooks dedicados
 * para encapsular la invalidación cruzada.
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

// ─── Notas de Crédito ─────────────────────────────────────────────────

export function useNotasCredito(filtros: ListarNotasCreditoFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.notasCreditoList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/cuentas-por-pagar/notas-credito?${query}`
        : '/api/v1/cuentas-por-pagar/notas-credito';
      const { data } = await apiRequest<PagedResponse<NotaCreditoListItem>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export function useNotaCredito(id: string | undefined) {
  return useQuery({
    queryKey: id
      ? cxpKeys.notaCreditoById(id)
      : ['cxp', 'nota-credito', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<NotaCreditoDetalle>(
        `/api/v1/cuentas-por-pagar/notas-credito/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useCapturarNotaCredito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CapturarNotaCreditoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CapturarNotaCreditoResponse>(
        '/api/v1/cuentas-por-pagar/notas-credito',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.notasCredito() });
    },
  });
}

export function useVincularFacturaNotaCredito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: VincularFacturaNotaCreditoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<VincularFacturaNotaCreditoResponse>(
        `/api/v1/cuentas-por-pagar/notas-credito/${args.id}/vincular-factura`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.notasCredito() });
    },
  });
}

// ─── Anticipos ────────────────────────────────────────────────────────

export function useAnticipos(filtros: ListarAnticiposFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.anticiposList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/cuentas-por-pagar/anticipos?${query}`
        : '/api/v1/cuentas-por-pagar/anticipos';
      const { data } = await apiRequest<PagedResponse<AnticipoListItem>>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useCapturarAnticipo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CapturarAnticipoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CapturarAnticipoResponse>(
        '/api/v1/cuentas-por-pagar/anticipos',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.anticipos() });
    },
  });
}

// ─── Notas de Cargo ───────────────────────────────────────────────────

export function useNotasCargo(filtros: ListarNotasCargoFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.notasCargoList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/cuentas-por-pagar/notas-cargo?${query}`
        : '/api/v1/cuentas-por-pagar/notas-cargo';
      const { data } = await apiRequest<PagedResponse<NotaCargoListItem>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export function useNotaCargo(id: string | undefined) {
  return useQuery({
    queryKey: id ? cxpKeys.notaCargoById(id) : ['cxp', 'nota-cargo', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<NotaCargoDetalle>(
        `/api/v1/cuentas-por-pagar/notas-cargo/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useCrearNotaCargo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearNotaCargoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CrearNotaCargoResponse>(
        '/api/v1/cuentas-por-pagar/notas-cargo',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.notasCargo() });
    },
  });
}

export function useAutorizarNotaCargo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/cuentas-por-pagar/notas-cargo/${args.id}/autorizar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.notasCargo() });
    },
  });
}

export function useAplicarNotaCargo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/cuentas-por-pagar/notas-cargo/${args.id}/aplicar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.notasCargo() });
    },
  });
}

// ─── Aplicación inline a factura ──────────────────────────────────────

export function useAplicarNcAFactura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      facturaId: string;
      facturaVersionEsperada: number;
      command: AplicarNotaCreditoAFacturaCommand;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/cuentas-por-pagar/facturas/${args.facturaId}/aplicar-nc`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
          headers: {
            'X-Expected-Version': String(args.facturaVersionEsperada),
          },
        },
      );
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.facturaById(vars.facturaId),
      });
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.notasCredito() });
    },
  });
}

export function useAplicarAnticipoAFactura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      facturaId: string;
      facturaVersionEsperada: number;
      command: AplicarAnticipoAFacturaCommand;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/cuentas-por-pagar/facturas/${args.facturaId}/aplicar-anticipo`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
          headers: {
            'X-Expected-Version': String(args.facturaVersionEsperada),
          },
        },
      );
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.facturaById(vars.facturaId),
      });
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.anticipos() });
    },
  });
}
