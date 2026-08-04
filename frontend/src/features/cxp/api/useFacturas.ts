import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxpKeys, type ListarFacturasFiltros } from '@/features/cxp/api/keys';
import type {
  AutorizarFacturaResponse,
  CancelarFacturaCommand,
  CancelarFacturaResponse,
  CapturarFacturaConOcCommand,
  CapturarFacturaConOcResponse,
  EditarCabeceraFacturaCommand,
  EditarCabeceraFacturaResponse,
  FacturaDetalle,
  FacturaListItem,
  PagedResponse,
} from '@/features/cxp/api/types';

/**
 * Hooks de Facturas de proveedor (FE-F2-PR1). Cubre bandeja paginada +
 * detalle + 5 mutaciones: capturar con OC, editar cabecera, autorizar,
 * cancelar. Todas las mutaciones llevan Idempotency-Key (ADR-0020) y,
 * las que modifican estado, también <c>X-Expected-Version</c>
 * (ADR-0012 capa 1 — el backend aún no migró a If-Match estándar).
 *
 * <para>PLATFORM-TODO(&lt;FacturasIfMatchMigracion&gt;): cuando F4
 * backend migre de <c>X-Expected-Version</c> a <c>If-Match</c>
 * estándar (ETag), reemplazar el header manual por
 * <c>ifMatch: etag</c> del cliente.</para>
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

export function useFacturas(filtros: ListarFacturasFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.facturasList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/cuentas-por-pagar/facturas?${query}`
        : '/api/v1/cuentas-por-pagar/facturas';
      const { data } = await apiRequest<PagedResponse<FacturaListItem>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export function useFactura(id: string | undefined) {
  return useQuery({
    queryKey: id ? cxpKeys.facturaById(id) : ['cxp', 'factura', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<FacturaDetalle>(
        `/api/v1/cuentas-por-pagar/facturas/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useCapturarFacturaConOc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CapturarFacturaConOcCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CapturarFacturaConOcResponse>(
        '/api/v1/cuentas-por-pagar/facturas',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
    },
  });
}

export function useEditarCabeceraFactura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: EditarCabeceraFacturaCommand;
    }) => {
      const { data } = await apiRequest<EditarCabeceraFacturaResponse>(
        `/api/v1/cuentas-por-pagar/facturas/${args.id}`,
        {
          method: 'PATCH',
          body: args.command,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.facturaById(vars.id),
      });
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
    },
  });
}

export function useAutorizarFactura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AutorizarFacturaResponse>(
        `/api/v1/cuentas-por-pagar/facturas/${args.id}/autorizar`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.facturaById(vars.id),
      });
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
    },
  });
}

export function useCancelarFactura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: CancelarFacturaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CancelarFacturaResponse>(
        `/api/v1/cuentas-por-pagar/facturas/${args.id}/cancelar`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.facturaById(vars.id),
      });
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
    },
  });
}
