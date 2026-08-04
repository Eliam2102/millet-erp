import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  cxpKeys,
  type ListarAprobadoresLimitesFiltros,
  type ListarPoliticasViaticosFiltros,
} from '@/features/cxp/api/keys';
import type {
  ActualizarAprobadorLimiteCommand,
  ActualizarPoliticaViaticosCommand,
  AprobadorLimite,
  CrearAprobadorLimiteCommand,
  CrearPoliticaViaticosCommand,
  PagedResponse,
  PoliticaViaticos,
} from '@/features/cxp/api/types';

/**
 * Hooks de catálogos admin CxP (cxp-fe/admin-pages). Cubre los
 * endpoints <c>CatalogosCxpAdminEndpoints</c> del backend (F7-PR3):
 * <list>
 *   <item><b>Aprobadores con límite</b>: list + crear + actualizar
 *         monto + cerrar.</item>
 *   <item><b>Políticas de viáticos</b>: list + crear + actualizar.</item>
 * </list>
 *
 * <para>PLATFORM-TODO(&lt;CxpTolerancias&gt;): backend no tiene endpoint
 * de tolerancias por proveedor en el módulo CxP; la permission canónica
 * <c>cuentas_por_pagar.proveedores.ajustar-tolerancia</c> existe pero
 * el endpoint vive en Datos Maestros (cuando se construya).</para>
 */

function buildQuery(filtros: object): string {
  const sp = new URLSearchParams();
  for (const [key, value] of Object.entries(filtros)) {
    if (value !== undefined && value !== null && value !== '') {
      sp.set(key, String(value));
    }
  }
  return sp.toString();
}

// ─── Aprobadores con límite ───────────────────────────────────────────

export function useAprobadoresLimites(
  filtros: ListarAprobadoresLimitesFiltros = {},
) {
  return useQuery({
    queryKey: cxpKeys.aprobadoresLimitesList(filtros),
    queryFn: async ({ signal }) => {
      const q = buildQuery(filtros);
      const path = q
        ? `/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites?${q}`
        : '/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites';
      const { data } = await apiRequest<PagedResponse<AprobadorLimite>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export function useCrearAprobadorLimite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearAprobadorLimiteCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AprobadorLimite>(
        '/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.aprobadoresLimites(),
      });
    },
  });
}

export function useActualizarAprobadorLimite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: ActualizarAprobadorLimiteCommand;
    }) => {
      const { data } = await apiRequest<AprobadorLimite>(
        `/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites/${args.id}`,
        {
          method: 'PATCH',
          body: args.command,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.aprobadoresLimites(),
      });
    },
  });
}

export function useCerrarAprobadorLimite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      fecha: string;
    }) => {
      const { data } = await apiRequest<AprobadorLimite>(
        `/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites/${args.id}/cerrar`,
        {
          method: 'POST',
          body: { fecha: args.fecha },
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.aprobadoresLimites(),
      });
    },
  });
}

// ─── Políticas de viáticos ────────────────────────────────────────────

export function usePoliticasViaticos(
  filtros: ListarPoliticasViaticosFiltros = {},
) {
  return useQuery({
    queryKey: cxpKeys.politicasViaticosList(filtros),
    queryFn: async ({ signal }) => {
      const q = buildQuery(filtros);
      const path = q
        ? `/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos?${q}`
        : '/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos';
      const { data } = await apiRequest<PagedResponse<PoliticaViaticos>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export function useCrearPoliticaViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearPoliticaViaticosCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PoliticaViaticos>(
        '/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.politicasViaticos() });
    },
  });
}

export function useActualizarPoliticaViaticos() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: ActualizarPoliticaViaticosCommand;
    }) => {
      const { data } = await apiRequest<PoliticaViaticos>(
        `/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos/${args.id}`,
        {
          method: 'PATCH',
          body: args.command,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxpKeys.politicasViaticos() });
    },
  });
}
