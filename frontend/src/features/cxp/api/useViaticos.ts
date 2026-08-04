import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxpKeys, type ListarViaticosFiltros } from '@/features/cxp/api/keys';
import type {
  CapturarComprobacionViaticosCommand,
  CapturarComprobacionViaticosResponse,
  LiberarComprobacionViaticosResponse,
  PagedResponse,
  RechazarSolicitudViaticosCommand,
  SolicitarAnticipoViaticosCommand,
  SolicitarAnticipoViaticosResponse,
  SolicitudViaticosDetalle,
  SolicitudViaticosListItem,
  TransicionViaticosResponse,
} from '@/features/cxp/api/types';

/**
 * Hooks del flujo de Viáticos Electrónicos (FE-F5-PR1). Cubre el ciclo
 * completo end-to-end documentado en backend ViaticosEndpoints F7-PR3:
 * <list>
 *   <item><b>Solicitar</b>: empleado captura monto + destino + fechas.</item>
 *   <item><b>autorizar-jefe</b>: jefe directo firma.</item>
 *   <item><b>autorizar-df</b>: DF firma si excede política.</item>
 *   <item><b>marcar-pagado</b>: proxy Tesorería entrega anticipo.</item>
 *   <item><b>capturar-comprobacion</b>: empleado al regreso.</item>
 *   <item><b>liberar</b>: CxP genera facturas y calcula diferencia.</item>
 *   <item><b>rechazar</b>: jefe o DF antes del anticipo.</item>
 * </list>
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

export function useViaticos(filtros: ListarViaticosFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.viaticosList(filtros),
    queryFn: async ({ signal }) => {
      const q = buildQuery(filtros);
      const path = q
        ? `/api/v1/cuentas-por-pagar/viaticos?${q}`
        : '/api/v1/cuentas-por-pagar/viaticos';
      const { data } = await apiRequest<
        PagedResponse<SolicitudViaticosListItem>
      >(path, { signal });
      return data;
    },
  });
}

export function useSolicitudViaticos(id: string | undefined) {
  return useQuery({
    queryKey: id ? cxpKeys.viaticoById(id) : ['cxp', 'viatico', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<SolicitudViaticosDetalle>(
        `/api/v1/cuentas-por-pagar/viaticos/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useSolicitarAnticipoViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: SolicitarAnticipoViaticosCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<SolicitarAnticipoViaticosResponse>(
        '/api/v1/cuentas-por-pagar/viaticos',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.viaticos() });
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
      const { data } = await apiRequest<TransicionViaticosResponse>(
        `/api/v1/cuentas-por-pagar/viaticos/${args.id}/${suffix}`,
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

export function useAutorizarPorJefeViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    ...transitionMutation('autorizar-jefe'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.viaticos() });
    },
  });
}

export function useAutorizarPorDfViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    ...transitionMutation('autorizar-df'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.viaticos() });
    },
  });
}

export function useMarcarAnticipoPagadoViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    ...transitionMutation('marcar-pagado'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.viaticos() });
    },
  });
}

export function useCapturarComprobacionViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: CapturarComprobacionViaticosCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CapturarComprobacionViaticosResponse>(
        `/api/v1/cuentas-por-pagar/viaticos/${args.id}/capturar-comprobacion`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.viaticos() });
    },
  });
}

export function useLiberarComprobacionViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<LiberarComprobacionViaticosResponse>(
        `/api/v1/cuentas-por-pagar/viaticos/${args.id}/liberar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.viaticos() });
    },
  });
}

export function useRechazarSolicitudViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: RechazarSolicitudViaticosCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<TransicionViaticosResponse>(
        `/api/v1/cuentas-por-pagar/viaticos/${args.id}/rechazar`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.viaticos() });
    },
  });
}
