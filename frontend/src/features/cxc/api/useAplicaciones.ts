import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type {
  CrearPropuestaAplicacionCommand,
  FacturaAbiertaItem,
  PagedResponse,
  PropuestaAplicacionResponse,
} from '@/features/cxc/api/types';

const BASE = '/api/v1/cuentas-por-cobrar/propuestas-aplicacion';

export interface ListarPropuestasFiltros {
  estado?: number;
  clienteId?: string;
  offset?: number;
  limit?: number;
}

/** <c>usePropuestasAplicacion(filtros)</c> — bandeja P1 (CXC-PR7). */
export function usePropuestasAplicacion(filtros: ListarPropuestasFiltros = {}) {
  return useQuery<PagedResponse<PropuestaAplicacionResponse>>({
    queryKey: cxcKeys.propuestasList({
      estado: filtros.estado ?? null,
      clienteId: filtros.clienteId ?? null,
      offset: filtros.offset ?? 0,
      limit: filtros.limit ?? 200,
    }),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<PropuestaAplicacionResponse>>(
        qs ? `${BASE}?${qs}` : BASE,
        { signal },
      );
      return data;
    },
  });
}

/** <c>usePropuestaAplicacion(id)</c> — detalle P3. */
export function usePropuestaAplicacion(id: string | null) {
  return useQuery<PropuestaAplicacionResponse>({
    queryKey: cxcKeys.propuestaById(id ?? ''),
    enabled: id != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<PropuestaAplicacionResponse>(
        `${BASE}/${id}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useFacturasAbiertas(clienteId, moneda)</c> — facturas vivas para el
 * matching (§4.1). Sin cliente la query no se dispara.
 */
export function useFacturasAbiertas(
  clienteId: string | null,
  moneda: string | null,
) {
  return useQuery<FacturaAbiertaItem[]>({
    queryKey: cxcKeys.facturasAbiertas(clienteId ?? '', moneda),
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      params.set('clienteId', clienteId!);
      if (moneda) params.set('moneda', moneda);
      const { data } = await apiRequest<FacturaAbiertaItem[]>(
        `/api/v1/cuentas-por-cobrar/cartera/facturas-abiertas?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useToleranciasNoFiscal()</c> — tolerancias por moneda (config
 * backend, provisional MXN=1000/USD=50). Cachea 1 h: la config no
 * cambia dentro de una sesión.
 */
export function useToleranciasNoFiscal() {
  return useQuery<Record<string, number>>({
    queryKey: cxcKeys.tolerancias(),
    staleTime: 60 * 60 * 1000,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<Record<string, number>>(
        `${BASE}/tolerancias`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useCrearPropuestaAplicacion()</c> — POST con Idempotency-Key. */
export function useCrearPropuestaAplicacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearPropuestaAplicacionCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PropuestaAplicacionResponse>(BASE, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.propuestas() });
    },
  });
}

/** <c>useConfirmarPropuesta()</c> — Ingresos confirma (interino A2). */
export function useConfirmarPropuesta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PropuestaAplicacionResponse>(
        `${BASE}/${args.id}/confirmar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.propuestas() });
    },
  });
}

/** <c>useRechazarPropuesta()</c> — Ingresos rechaza con motivo. */
export function useRechazarPropuesta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      motivo: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PropuestaAplicacionResponse>(
        `${BASE}/${args.id}/rechazar`,
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
      queryClient.invalidateQueries({ queryKey: cxcKeys.propuestas() });
    },
  });
}
