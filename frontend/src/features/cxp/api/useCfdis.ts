import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { apiFetch } from '@/lib/auth/api-client';
import { ApiError } from '@/lib/api/error';
import { cxpKeys, type ListarCfdisFiltros } from '@/features/cxp/api/keys';
import type {
  CfdiDetalle,
  CfdiListItem,
  CfdiParseado,
  DescartarCfdiCommand,
  IngresarCfdiResponse,
  MarcarCfdiDuplicadoCommand,
  PagedResponse,
} from '@/features/cxp/api/types';

/**
 * Hooks de CFDIs recibidos (FE-F1-PR1). Cubre la bandeja paginada con
 * filtros server-side + la carga manual de respaldo
 * (<c>POST /cfdis/cargar</c>, multipart) + las dos acciones de gestión:
 * <c>descartar</c> y <c>marcar-duplicado</c>. Todas las mutaciones llevan
 * Idempotency-Key (ADR-0020) y invalidan <c>cxpKeys.cfdis()</c>.
 *
 * <para>PLATFORM-TODO(&lt;CfdiBlobDownload&gt;): el backend aún no
 * expone <c>GET /cfdis/{id}</c> ni endpoints de descarga de XML/PDF.
 * Cuando se agreguen, este archivo gana <c>useCfdi(id)</c> +
 * <c>useCfdiXml(id)</c> + <c>useCfdiPdf(id)</c>.</para>
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

export function useCfdis(filtros: ListarCfdisFiltros = {}) {
  return useQuery({
    queryKey: cxpKeys.cfdisList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/cuentas-por-pagar/cfdis?${query}`
        : '/api/v1/cuentas-por-pagar/cfdis';
      const { data } = await apiRequest<PagedResponse<CfdiListItem>>(path, {
        signal,
      });
      return data;
    },
  });
}

/**
 * XML del CFDI re-parseado on-demand (<c>GET /cfdis/{id}/parseado</c>).
 * Alimenta el pre-llenado de encabezado y la importación de líneas en
 * <c>CapturarFacturaSheet</c>. Deshabilitado sin id (sheet sin CFDI).
 */
export function useCfdiParseado(id: string | null) {
  return useQuery({
    queryKey: cxpKeys.cfdiParseado(id ?? ''),
    enabled: id !== null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CfdiParseado>(
        `/api/v1/cuentas-por-pagar/cfdis/${id}/parseado`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * Fetch imperativo del parseado, para handlers de vinculación (NC y
 * anticipos prellenan la relación al seleccionar/cargar el CFDI sin
 * montar una query).
 */
export async function fetchCfdiParseado(id: string): Promise<CfdiParseado> {
  const { data } = await apiRequest<CfdiParseado>(
    `/api/v1/cuentas-por-pagar/cfdis/${id}/parseado`,
  );
  return data;
}

/** Fetch imperativo del detalle — prellenado tras cargar XML nuevo. */
export async function fetchCfdiDetalle(id: string): Promise<CfdiDetalle> {
  const { data } = await apiRequest<CfdiDetalle>(
    `/api/v1/cuentas-por-pagar/cfdis/${id}`,
  );
  return data;
}

/** Detalle de un CFDI (<c>GET /cfdis/{id}</c>) para el viewer. */
export function useCfdiDetalle(id: string | null) {
  return useQuery({
    queryKey: cxpKeys.cfdiDetalle(id ?? ''),
    enabled: id !== null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CfdiDetalle>(
        `/api/v1/cuentas-por-pagar/cfdis/${id}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * PDF del CFDI como object URL para <c>&lt;embed&gt;</c> (mismo molde que
 * <c>usePdfOrdenCompra</c> de Compras: el endpoint exige Bearer token,
 * así que no se puede apuntar el embed directo al endpoint).
 */
export function useCfdiPdfUrl(id: string | null, enabled: boolean) {
  return useQuery({
    queryKey: cxpKeys.cfdiPdfUrl(id ?? ''),
    enabled: enabled && id !== null,
    staleTime: Infinity,
    gcTime: 5 * 60 * 1000,
    retry: false,
    queryFn: async ({ signal }) => {
      const response = await apiFetch(
        `/api/v1/cuentas-por-pagar/cfdis/${id}/pdf`,
        { signal },
      );
      if (!response.ok) {
        let problem;
        try {
          problem = await response.json();
        } catch {
          problem = {
            type: 'about:blank',
            title: response.statusText || 'Error al descargar PDF',
            status: response.status,
            code: 'PDF_FETCH_ERROR',
          };
        }
        throw new ApiError(problem, response.status);
      }
      const blob = await response.blob();
      return URL.createObjectURL(blob);
    },
  });
}

export function useCargarCfdiManual() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      xml: File;
      pdf: File | null;
      idempotencyKey: string;
    }) => {
      const formData = new FormData();
      formData.append('xml', args.xml);
      if (args.pdf) {
        formData.append('pdf', args.pdf);
      }
      const { data } = await apiRequest<IngresarCfdiResponse>(
        '/api/v1/cuentas-por-pagar/cfdis/cargar',
        {
          method: 'POST',
          body: formData,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.cfdis() });
    },
  });
}

export function useDescartarCfdi() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      command: DescartarCfdiCommand;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/cuentas-por-pagar/cfdis/${args.id}/descartar`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.cfdis() });
    },
  });
}

export function useMarcarCfdiDuplicado() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      command: MarcarCfdiDuplicadoCommand;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/cuentas-por-pagar/cfdis/${args.id}/marcar-duplicado`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.cfdis() });
    },
  });
}
