import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  cxpKeys,
  type ListarEstadosCuentaTcFiltros,
} from '@/features/cxp/api/keys';
import type {
  CapturarMovimientoDesdeLineaCommand,
  CerrarEstadoCuentaTcResponse,
  ConciliarAutomaticoResponse,
  ConfirmarMatchLineaBancoCommand,
  CrearEstadoCuentaTcCommand,
  DisputarMovimientoTcCommand,
  EstadoCuentaTc,
  MovimientoEspecialResponse,
  MovimientoRefundResponse,
  MovimientoTc,
  PagedResponse,
  RegistrarMovimientoEspecialTcCommand,
  RegistrarRefundTcCommand,
  ResolverDisputaMovimientoTcCommand,
  SubirArchivoEstadoCuentaTcResponse,
} from '@/features/cxp/api/types';

/**
 * Hooks del ciclo cierre TC (FE-F6-PR2). Cubre:
 * <list>
 *   <item><b>Estados de cuenta</b>: crear + subir archivo + conciliar
 *         automático + confirmar matches sugeridos + captura
 *         retroactiva + marcar conciliado + cerrar + marcar pagado
 *         banco.</item>
 *   <item><b>Refunds</b>: registrar reembolso de proveedor a TC.</item>
 *   <item><b>Movimientos especiales</b>: intereses, anualidad, comisión
 *         por divisa.</item>
 *   <item><b>Disputas</b>: marcar y resolver disputa sobre cargo.</item>
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

export function useEstadosCuentaTc(filtros: ListarEstadosCuentaTcFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.estadosCuentaTcList(filtros),
    queryFn: async ({ signal }) => {
      const q = buildQuery(filtros);
      const path = q
        ? `/api/v1/cuentas-por-pagar/estados-cuenta-tc?${q}`
        : '/api/v1/cuentas-por-pagar/estados-cuenta-tc';
      const { data } = await apiRequest<PagedResponse<EstadoCuentaTc>>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useCrearEstadoCuentaTc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearEstadoCuentaTcCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EstadoCuentaTc>(
        '/api/v1/cuentas-por-pagar/estados-cuenta-tc',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.estadosCuentaTc() });
    },
  });
}

export function useSubirArchivoEstadoCuentaTc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      archivo: File;
      idempotencyKey: string;
    }) => {
      const formData = new FormData();
      formData.append('archivo', args.archivo);
      const { data } = await apiRequest<SubirArchivoEstadoCuentaTcResponse>(
        `/api/v1/cuentas-por-pagar/estados-cuenta-tc/${args.id}/archivo`,
        {
          method: 'POST',
          body: formData,
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.estadosCuentaTc() });
      queryClient.invalidateQueries({
        queryKey: cxpKeys.estadoCuentaTcById(vars.id),
      });
    },
  });
}

export function useConciliarAutomaticoEstadoCuentaTc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ConciliarAutomaticoResponse>(
        `/api/v1/cuentas-por-pagar/estados-cuenta-tc/${args.id}/conciliar-automatico`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.estadosCuentaTc() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.movimientosTc() });
    },
  });
}

export function useConfirmarMatchLineaBanco() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      ecId: string;
      lineaId: string;
      versionEsperada: number;
      command: ConfirmarMatchLineaBancoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EstadoCuentaTc>(
        `/api/v1/cuentas-por-pagar/estados-cuenta-tc/${args.ecId}/lineas/${args.lineaId}/confirmar-match`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.estadosCuentaTc() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.movimientosTc() });
    },
  });
}

export function useCapturarMovimientoDesdeLinea() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      ecId: string;
      lineaId: string;
      versionEsperada: number;
      command: CapturarMovimientoDesdeLineaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EstadoCuentaTc>(
        `/api/v1/cuentas-por-pagar/estados-cuenta-tc/${args.ecId}/lineas/${args.lineaId}/capturar-retroactiva`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.estadosCuentaTc() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.movimientosTc() });
    },
  });
}

export function useMarcarEstadoCuentaConciliado() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EstadoCuentaTc>(
        `/api/v1/cuentas-por-pagar/estados-cuenta-tc/${args.id}/marcar-conciliado`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.estadosCuentaTc() });
    },
  });
}

export function useCerrarEstadoCuentaTc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CerrarEstadoCuentaTcResponse>(
        `/api/v1/cuentas-por-pagar/estados-cuenta-tc/${args.id}/cerrar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.estadosCuentaTc() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
    },
  });
}

export function useMarcarEstadoCuentaPagadoBanco() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EstadoCuentaTc>(
        `/api/v1/cuentas-por-pagar/estados-cuenta-tc/${args.id}/marcar-pagado-banco`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.estadosCuentaTc() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.movimientosTc() });
    },
  });
}

// ─── Refunds + Movimientos Especiales + Disputas ──────────────────────

export function useRegistrarRefundTc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarRefundTcCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<MovimientoRefundResponse>(
        '/api/v1/cuentas-por-pagar/movimientos-tc/refund',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.movimientosTc() });
    },
  });
}

export function useRegistrarMovimientoEspecialTc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarMovimientoEspecialTcCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<MovimientoEspecialResponse>(
        '/api/v1/cuentas-por-pagar/movimientos-tc/especial',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.movimientosTc() });
    },
  });
}

export function useDisputarMovimientoTc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: DisputarMovimientoTcCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<MovimientoTc>(
        `/api/v1/cuentas-por-pagar/movimientos-tc/${args.id}/disputar`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.movimientosTc() });
    },
  });
}

export function useResolverDisputaMovimientoTc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: ResolverDisputaMovimientoTcCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<MovimientoTc>(
        `/api/v1/cuentas-por-pagar/movimientos-tc/${args.id}/resolver-disputa`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.movimientosTc() });
    },
  });
}
