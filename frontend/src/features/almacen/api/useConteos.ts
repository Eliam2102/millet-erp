import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type ListarConteosFiltros,
} from '@/features/almacen/api/keys';
import type {
  ConteoDetalle,
  ConteoListItem,
  CrearConteoCommand,
  CrearConteoResponse,
  LineaConteoParaCapturarDto,
  PagedResponse,
} from '@/features/almacen/api/types';

/**
 * Hooks de inventario físico (FE-F5-PR1, captura sin sesgo).
 *
 * <para><b>CRÍTICO — A6 captura sin sesgo</b>: las líneas para
 * capturar (<c>useLineasParaCapturar</c>) NO incluyen
 * <c>cantidadTeorica</c>. El endpoint backend exige
 * <c>almacen.inventarios.capturar</c> y SIN
 * <c>aprobar-nivel1+</c> — eso es la separación de permisos que
 * garantiza el sin-sesgo.</para>
 *
 * <para>La comparación (aprobador, con teórico) vive en
 * <c>useLineasComparacion</c> y se agrega en FE-F5-PR2.</para>
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

export function useConteos(filtros: ListarConteosFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.conteosList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/conteos?${query}`
        : '/api/v1/almacen/conteos';
      const { data } = await apiRequest<PagedResponse<ConteoListItem>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export function useConteo(id: string | undefined) {
  return useQuery({
    queryKey: id ? almacenKeys.conteoById(id) : ['almacen', 'conteo', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ConteoDetalle>(
        `/api/v1/almacen/conteos/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useCrearConteo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearConteoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CrearConteoResponse>(
        '/api/v1/almacen/conteos',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.conteos() });
    },
  });
}

export function useIniciarConteo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { conteoId: string }) => {
      await apiRequest<void>(
        `/api/v1/almacen/conteos/${args.conteoId}/iniciar`,
        { method: 'POST', body: {} },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.conteos() });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.conteoId),
      });
    },
  });
}

/**
 * <b>SIN cantidad_teorica</b>: el contador captura sin sesgo. Endpoint
 * separado del de comparación (que SÍ tiene teórico) — distinto
 * permiso.
 */
export function useLineasParaCapturar(conteoId: string | undefined) {
  return useQuery({
    queryKey: conteoId
      ? almacenKeys.lineasParaCapturar(conteoId)
      : ['almacen', 'conteo', 'lineas', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<
        readonly LineaConteoParaCapturarDto[]
      >(
        `/api/v1/almacen/conteos/${conteoId}/lineas-para-capturar`,
        { signal },
      );
      return data;
    },
    enabled: conteoId != null,
  });
}

export function useCapturarLinea() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      conteoId: string;
      lineaId: string;
      cantidadReal: number;
    }) => {
      await apiRequest<void>(
        `/api/v1/almacen/conteos/${args.conteoId}/lineas/${args.lineaId}/capturar`,
        {
          method: 'POST',
          body: { cantidadReal: args.cantidadReal },
        },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.lineasParaCapturar(args.conteoId),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.conteoId),
      });
    },
  });
}

export function useEnviarConteoAConciliacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { conteoId: string }) => {
      await apiRequest<void>(
        `/api/v1/almacen/conteos/${args.conteoId}/enviar-a-conciliacion`,
        { method: 'POST', body: {} },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.conteos() });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.conteoById(args.conteoId),
      });
    },
  });
}
