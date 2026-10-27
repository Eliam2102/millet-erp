import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type {
  AutorizarLiberacionPendienteBody,
  AutorizacionCreditoResponse,
  BeneficiarioCreditoResponse,
  CrearAutorizacionCreditoCommand,
  DecidirLiberacionCommand,
  DecisionLiberacionResponse,
  PagedResponse,
  RechazarLiberacionPendienteBody,
  ReglaLiberacionSerieResponse,
} from '@/features/cxc/api/types';

const BASE = '/api/v1/cuentas-por-cobrar/liberaciones';
const BASE_AUT = '/api/v1/cuentas-por-cobrar/autorizaciones';

export interface ListarDecisionesFiltros {
  pedidoRef?: string;
  clienteId?: string;
  resultado?: number;
  offset?: number;
  limit?: number;
}

/**
 * <c>useDecisionesLiberacion(filtros)</c> — bandeja P2 de decisiones
 * (CXC-PR4). <c>pedidoRef</c> es match EXACTO en el backend; la búsqueda
 * "contiene" del topbar se aplica client-side en la página.
 */
export function useDecisionesLiberacion(filtros: ListarDecisionesFiltros = {}) {
  return useQuery<PagedResponse<DecisionLiberacionResponse>>({
    queryKey: cxcKeys.liberacionesList({
      pedidoRef: filtros.pedidoRef ?? null,
      clienteId: filtros.clienteId ?? null,
      resultado: filtros.resultado ?? null,
      offset: filtros.offset ?? 0,
      limit: filtros.limit ?? 200,
    }),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.pedidoRef) params.set('pedidoRef', filtros.pedidoRef);
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.resultado != null)
        params.set('resultado', String(filtros.resultado));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<DecisionLiberacionResponse>>(
        qs ? `${BASE}?${qs}` : BASE,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useDecidirLiberacion()</c> — POST decidir (Idempotency-Key). El
 * backend evalúa la cascada serie → crédito → override y devuelve el
 * resultado; la decisión es INMUTABLE (correcciones = nueva decisión).
 */
export function useDecidirLiberacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: DecidirLiberacionCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<DecisionLiberacionResponse>(BASE, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      // El override consumido también cambia la lista de autorizaciones.
      queryClient.invalidateQueries({ queryKey: cxcKeys.liberaciones() });
      queryClient.invalidateQueries({ queryKey: cxcKeys.autorizaciones() });
      queryClient.invalidateQueries({ queryKey: [...cxcKeys.all, 'credito-disponible'] });
    },
  });
}

/**
 * <c>useAutorizarLiberacionPendiente()</c> — C7.3: el gerente de CxC
 * (<c>liberacion.override</c>) autoriza con motivo una decisión
 * PendienteAutorizacion (cobertura 50–99 %). El backend recalcula el
 * crédito y emite una nueva decisión ligada a la pendiente: Liberado (ya
 * alcanza), Retenido (bajó de 50 % o no hay línea activa) o
 * LiberadoConOverride (crea y consume la autorización). 409 si ya se
 * resolvió; 422 si quien autoriza es quien registró la pendiente.
 */
export function useAutorizarLiberacionPendiente() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      decisionId: string;
      body: AutorizarLiberacionPendienteBody;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<DecisionLiberacionResponse>(
        `${BASE}/${args.decisionId}/autorizar`,
        {
          method: 'POST',
          body: args.body,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.liberaciones() });
      queryClient.invalidateQueries({ queryKey: cxcKeys.autorizaciones() });
    },
  });
}

/**
 * <c>useRechazarLiberacionPendiente()</c> — C7.3: el gerente de CxC
 * rechaza con motivo una pendiente; el backend emite una decisión Retenido
 * ligada a ella. Mismos 409/422 que autorizar.
 */
export function useRechazarLiberacionPendiente() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      decisionId: string;
      body: RechazarLiberacionPendienteBody;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<DecisionLiberacionResponse>(
        `${BASE}/${args.decisionId}/rechazar`,
        {
          method: 'POST',
          body: args.body,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.liberaciones() });
    },
  });
}

/**
 * Usuarios con permiso de decidir liberaciones (gate: <c>liberacion.override</c>).
 * Sirve para elegir beneficiario y para mostrar el nombre de quien evaluó
 * una pendiente en la bandeja (C7.3).
 */
export function useBeneficiariosCredito(options: { enabled?: boolean } = {}) {
  return useQuery<BeneficiarioCreditoResponse[]>({
    queryKey: cxcKeys.beneficiariosCredito(),
    enabled: options.enabled ?? true,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<BeneficiarioCreditoResponse[]>(
        '/api/v1/cuentas-por-cobrar/autorizaciones/beneficiarios',
        { signal },
      );
      return data;
    },
  });
}

export interface ListarAutorizacionesFiltros {
  estado?: number;
  beneficiarioUsuarioId?: string;
  offset?: number;
  limit?: number;
}

/**
 * C7.3: reglas activas por serie, para saber en el panel si el pedido
 * exige autorización aunque el crédito no alcance el 50 %.
 */
export function useReglasLiberacionSerie() {
  return useQuery<ReglaLiberacionSerieResponse[]>({
    queryKey: cxcKeys.reglasSerie(),
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ReglaLiberacionSerieResponse[]>(`${BASE}/reglas-serie`, {
        signal,
      });
      return data;
    },
  });
}

/**
 * <c>useAutorizacionesCredito(filtros)</c> — overrides consumibles
 * (gate de lectura: <c>liberacion.decidir</c>).
 */
export function useAutorizacionesCredito(
  filtros: ListarAutorizacionesFiltros = {},
  opts: { enabled?: boolean } = {},
) {
  return useQuery<PagedResponse<AutorizacionCreditoResponse>>({
    queryKey: cxcKeys.autorizacionesList({
      estado: filtros.estado ?? null,
      beneficiarioUsuarioId: filtros.beneficiarioUsuarioId ?? null,
      offset: filtros.offset ?? 0,
      limit: filtros.limit ?? 200,
    }),
    enabled: opts.enabled ?? true,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      if (filtros.beneficiarioUsuarioId)
        params.set('beneficiarioUsuarioId', filtros.beneficiarioUsuarioId);
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<AutorizacionCreditoResponse>>(
        qs ? `${BASE_AUT}?${qs}` : BASE_AUT,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useCrearAutorizacionCredito()</c> — el gerente
 * (<c>liberacion.override</c>) autoriza a un beneficiario específico;
 * nunca autoconsumo, nunca captura de contraseña ajena (§4.3).
 */
export function useCrearAutorizacionCredito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearAutorizacionCreditoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AutorizacionCreditoResponse>(BASE_AUT, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.autorizaciones() });
    },
  });
}

/** <c>useCancelarAutorizacionCredito()</c> — POST cancelar (supervisor). */
export function useCancelarAutorizacionCredito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AutorizacionCreditoResponse>(
        `${BASE_AUT}/${args.id}/cancelar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.autorizaciones() });
    },
  });
}
