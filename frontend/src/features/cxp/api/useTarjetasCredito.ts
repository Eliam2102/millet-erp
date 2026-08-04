import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  cxpKeys,
  type ListarMovimientosTcFiltros,
  type ListarTarjetasFiltros,
} from '@/features/cxp/api/keys';
import type {
  ActualizarTarjetaCommand,
  AgregarUsuarioAutorizadoCommand,
  BloquearTarjetaCommand,
  CrearTarjetaCommand,
  MovimientoTc,
  PagedResponse,
  RegistrarMovimientoTcConCfdiCommand,
  RegistrarMovimientoTcSinCfdiCommand,
  Tarjeta,
  UsuarioAutorizado,
} from '@/features/cxp/api/types';

/**
 * Hooks de Tarjetas de Crédito empresariales + Movimientos (FE-F6-PR1).
 * Cubre:
 * <list>
 *   <item><b>Tarjetas</b>: list + crear + actualizar + bloquear +
 *         reactivar + cancelar + usuarios autorizados.</item>
 *   <item><b>Movimientos</b>: list + Flujo A (con CFDI → genera
 *         FacturaProveedor Pagada) + Flujo B (sin CFDI → solo asiento).</item>
 * </list>
 *
 * <para>Refunds, disputas y movimientos especiales entran en FE-F6-PR2.</para>
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

// ─── Tarjetas ─────────────────────────────────────────────────────────

export function useTarjetas(filtros: ListarTarjetasFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.tarjetasList(filtros),
    queryFn: async ({ signal }) => {
      const q = buildQuery(filtros);
      const path = q
        ? `/api/v1/cuentas-por-pagar/tarjetas?${q}`
        : '/api/v1/cuentas-por-pagar/tarjetas';
      const { data } = await apiRequest<PagedResponse<Tarjeta>>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useCrearTarjeta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearTarjetaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<Tarjeta>(
        '/api/v1/cuentas-por-pagar/tarjetas',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.tarjetas() });
    },
  });
}

export function useActualizarTarjeta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: ActualizarTarjetaCommand;
    }) => {
      const { data } = await apiRequest<Tarjeta>(
        `/api/v1/cuentas-por-pagar/tarjetas/${args.id}`,
        {
          method: 'PATCH',
          body: args.command,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.tarjetas() });
    },
  });
}

export function useBloquearTarjeta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: BloquearTarjetaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<Tarjeta>(
        `/api/v1/cuentas-por-pagar/tarjetas/${args.id}/bloquear`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.tarjetas() });
    },
  });
}

export function useReactivarTarjeta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<Tarjeta>(
        `/api/v1/cuentas-por-pagar/tarjetas/${args.id}/reactivar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.tarjetas() });
    },
  });
}

export function useCancelarTarjeta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      fecha: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<Tarjeta>(
        `/api/v1/cuentas-por-pagar/tarjetas/${args.id}/cancelar`,
        {
          method: 'POST',
          body: { fecha: args.fecha },
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.tarjetas() });
    },
  });
}

export function useAgregarUsuarioAutorizado() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      tarjetaId: string;
      versionEsperada: number;
      command: AgregarUsuarioAutorizadoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<UsuarioAutorizado>(
        `/api/v1/cuentas-por-pagar/tarjetas/${args.tarjetaId}/usuarios`,
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.tarjetas() });
    },
  });
}

// ─── Movimientos TC ───────────────────────────────────────────────────

export function useMovimientosTc(filtros: ListarMovimientosTcFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.movimientosTcList(filtros),
    queryFn: async ({ signal }) => {
      const q = buildQuery(filtros);
      const path = q
        ? `/api/v1/cuentas-por-pagar/movimientos-tc?${q}`
        : '/api/v1/cuentas-por-pagar/movimientos-tc';
      const { data } = await apiRequest<PagedResponse<MovimientoTc>>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useRegistrarMovimientoTcConCfdi() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarMovimientoTcConCfdiCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<MovimientoTc>(
        '/api/v1/cuentas-por-pagar/movimientos-tc/con-cfdi',
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
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
    },
  });
}

export function useRegistrarMovimientoTcSinCfdi() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarMovimientoTcSinCfdiCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<MovimientoTc>(
        '/api/v1/cuentas-por-pagar/movimientos-tc/sin-cfdi',
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
