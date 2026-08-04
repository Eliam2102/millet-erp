import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type AlfakHistorialFiltros,
  type HijosJerarquiaParams,
  type ListarSaldosFiltros,
  type MpCnkFiltros,
} from '@/features/almacen/api/keys';
import type {
  AlfakHistorialFila,
  EjecutarCierreMensualCommand,
  EjecutarCierreMensualResponse,
  ExistenciaMpCnkFila,
  NodoJerarquiaSaldo,
  PagedResponse,
  ReporteResponse,
  SaldoListItem,
  SaldoUbicacionItem,
} from '@/features/almacen/api/types';

/**
 * Hooks de saldos, cierre de mes y reportes operativos (FE-F6-PR1).
 *
 * <list type="bullet">
 *   <item><c>useSaldos</c>: bandeja paginada de saldos materializados.
 *     Permiso <c>almacen.almacenes.leer</c>.</item>
 *   <item><c>useEjecutarCierreMes</c>: cierre mensual (Jefe Almacén).
 *     Permiso <c>almacen.cierre-mes.ejecutar</c>. Idempotency-Key
 *     obligatorio.</item>
 *   <item><c>useReporteAlfakHistorial</c>,
 *     <c>useReporteExistenciaMpCnk</c>: reportes operativos en
 *     shape canónico ADR-0036.</item>
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

export function useSaldos(filtros: ListarSaldosFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.saldosList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/saldos?${query}`
        : '/api/v1/almacen/saldos';
      const { data } = await apiRequest<PagedResponse<SaldoListItem>>(path, {
        signal,
      });
      return data;
    },
  });
}

/**
 * C7.2b: ubicaciones con existencia (>0) de un artículo, para el selector de
 * bin en salidas (incluida la ÚNICA). Salida-por-línea C2: el sub es opcional —
 * sin él (salida-con-RQ, que ya no tiene sub de cabecera) lista bins de todos
 * los subs; con él (vale, devolución) acota. Se dispara con solo el artículo.
 */
export function saldosPorUbicacionQueryOptions(
  articuloId: string | null | undefined,
  subAlmacenId: string | null | undefined,
) {
  return {
    queryKey: almacenKeys.saldosPorUbicacion(articuloId ?? '', subAlmacenId ?? ''),
    enabled: !!articuloId,
    queryFn: async ({ signal }: { signal?: AbortSignal }) => {
      const query = buildQuery({ articuloId, subAlmacenId });
      const { data } = await apiRequest<SaldoUbicacionItem[]>(
        `/api/v1/almacen/saldos/por-ubicacion?${query}`,
        { signal },
      );
      return data;
    },
  };
}

export function useSaldosPorUbicacion(
  articuloId: string | null | undefined,
  subAlmacenId: string | null | undefined,
) {
  return useQuery(saldosPorUbicacionQueryOptions(articuloId, subAlmacenId));
}

/**
 * PR6: hijos inmediatos de un nodo de la jerarquía de saldos, con rollup
 * (cantidad + valor). Carga perezosa — el árbol dispara una llamada por
 * expansión y TanStack Query cachea cada nodo por su key.
 */
export function useHijosJerarquia(params: HijosJerarquiaParams) {
  return useQuery({
    queryKey: almacenKeys.saldosJerarquia(params),
    queryFn: async ({ signal }) => {
      const query = buildQuery({
        // raíz = sin nodoTipo (el endpoint lo trata como default)
        nodoTipo: params.nodoTipo === 'raiz' ? undefined : params.nodoTipo,
        nodoId: params.nodoId,
        articuloId: params.articuloId,
        incluirVacios: params.incluirVacios ? true : undefined,
      });
      const path = query
        ? `/api/v1/almacen/saldos/jerarquia?${query}`
        : '/api/v1/almacen/saldos/jerarquia';
      const { data } = await apiRequest<NodoJerarquiaSaldo[]>(path, { signal });
      return data;
    },
  });
}

export function useEjecutarCierreMes() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: EjecutarCierreMensualCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EjecutarCierreMensualResponse>(
        '/api/v1/almacen/cierre-mes',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.all });
    },
  });
}

export function useReporteAlfakHistorial(filtros: AlfakHistorialFiltros) {
  return useQuery({
    queryKey: almacenKeys.reporteAlfak(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({
        desde: filtros.desde,
        hasta: filtros.hasta,
      });
      if (filtros.subAlmacenId) params.set('subAlmacenId', filtros.subAlmacenId);
      if (filtros.articuloId) params.set('articuloId', filtros.articuloId);
      const { data } = await apiRequest<ReporteResponse<AlfakHistorialFila>>(
        `/api/v1/almacen/reportes/alfak-historial-almacen?${params}`,
        { signal },
      );
      return data;
    },
    enabled: !!filtros.desde && !!filtros.hasta,
  });
}

export function useReporteExistenciaMpCnk(filtros: MpCnkFiltros) {
  return useQuery({
    queryKey: almacenKeys.reporteMpCnk(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.subAlmacenId) params.set('subAlmacenId', filtros.subAlmacenId);
      const query = params.toString();
      const path = query
        ? `/api/v1/almacen/reportes/mp-cnk?${query}`
        : '/api/v1/almacen/reportes/mp-cnk';
      const { data } = await apiRequest<ReporteResponse<ExistenciaMpCnkFila>>(
        path,
        { signal },
      );
      return data;
    },
  });
}
