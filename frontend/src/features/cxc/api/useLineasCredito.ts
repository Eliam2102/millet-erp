import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  cxcKeys,
  type ListarLineasCreditoFiltros,
} from '@/features/cxc/api/keys';
import type {
  ActualizarLineaCreditoBody,
  ClienteLookupCxcItem,
  CreditoDisponibleResponse,
  CrearLineaCreditoCommand,
  LineaCreditoResponse,
  PagedResponse,
} from '@/features/cxc/api/types';

const BASE = '/api/v1/cuentas-por-cobrar/lineas-credito';

/**
 * <c>useLineasCredito(filtros)</c> — bandeja paginada server-side de
 * líneas de crédito (CXC-PR1). El filtro <c>q</c> del topbar se aplica
 * client-side en la página (el backend filtra por clienteId / estado /
 * moneda).
 */
export function useLineasCredito(filtros: ListarLineasCreditoFiltros = {}) {
  return useQuery<PagedResponse<LineaCreditoResponse>>({
    queryKey: cxcKeys.lineasCreditoList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      if (filtros.moneda) params.set('moneda', filtros.moneda);
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<LineaCreditoResponse>>(
        qs ? `${BASE}?${qs}` : BASE,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useLineaCredito(id)</c> — detalle de una línea (master-detail P3). */
export function useLineaCredito(id: string | null) {
  return useQuery<LineaCreditoResponse>({
    queryKey: cxcKeys.lineaCreditoById(id ?? ''),
    enabled: id != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<LineaCreditoResponse>(
        `${BASE}/${id}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useCreditoDisponible(clienteId)</c> — evaluación por línea/moneda
 * (CXC-PR2/PR3) con bandera <c>datoIncompleto</c> (gap G1 A+W).
 */
export function useCreditoDisponible(clienteId: string | null) {
  return useQuery<CreditoDisponibleResponse>({
    queryKey: cxcKeys.creditoDisponible(clienteId ?? ''),
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CreditoDisponibleResponse>(
        `/api/v1/cuentas-por-cobrar/credito-disponible/${clienteId}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useClientesLookupCxc(filtros)</c> — lookup del módulo (CXC-FE-PR2):
 * búsqueda RFC/razón social para el selector, o <c>ids</c> para resolver
 * nombres de una página de bandeja.
 */
export function useClientesLookupCxc(
  filtros: { rfc?: string; razonSocial?: string; ids?: string[]; limit?: number },
  opts: { enabled?: boolean } = {},
) {
  return useQuery<ClienteLookupCxcItem[]>({
    queryKey: cxcKeys.clientesLookup({
      rfc: filtros.rfc ?? null,
      razonSocial: filtros.razonSocial ?? null,
      ids: filtros.ids ?? null,
      limit: filtros.limit ?? null,
    }),
    enabled: opts.enabled ?? true,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.rfc) params.set('rfc', filtros.rfc);
      if (filtros.razonSocial) params.set('razonSocial', filtros.razonSocial);
      if (filtros.ids != null && filtros.ids.length > 0)
        params.set('ids', filtros.ids.join(','));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<ClienteLookupCxcItem[]>(
        qs
          ? `/api/v1/cuentas-por-cobrar/clientes-lookup?${qs}`
          : '/api/v1/cuentas-por-cobrar/clientes-lookup',
        { signal },
      );
      return data;
    },
  });
}

// ─── Mutations (Idempotency-Key ADR-0020 + X-Expected-Version) ────────

/** <c>useCrearLineaCredito()</c> — POST con Idempotency-Key. */
export function useCrearLineaCredito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearLineaCreditoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<LineaCreditoResponse>(BASE, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.all });
    },
  });
}

/** <c>useActualizarLineaCredito()</c> — PUT límite/plazo/clasificación. */
export function useActualizarLineaCredito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      body: ActualizarLineaCreditoBody;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<LineaCreditoResponse>(
        `${BASE}/${args.id}`,
        {
          method: 'PUT',
          body: args.body,
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.all });
    },
  });
}

/** <c>useBloquearLineaCredito()</c> — POST bloquear con motivo. */
export function useBloquearLineaCredito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      motivo: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<LineaCreditoResponse>(
        `${BASE}/${args.id}/bloquear`,
        {
          method: 'POST',
          body: { motivo: args.motivo },
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.all });
    },
  });
}

/** <c>useDesbloquearLineaCredito()</c> — POST desbloquear. */
export function useDesbloquearLineaCredito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<LineaCreditoResponse>(
        `${BASE}/${args.id}/desbloquear`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.all });
    },
  });
}
