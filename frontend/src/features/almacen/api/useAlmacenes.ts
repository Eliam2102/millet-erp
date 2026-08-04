import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type ListarAlmacenesFiltros,
  type ListarSubAlmacenesFiltros,
} from '@/features/almacen/api/keys';
import type {
  AlmacenDetalle,
  AlmacenListItem,
  PagedResponse,
  SubAlmacenListItem,
} from '@/features/almacen/api/types';

/**
 * Hooks de Almacenes y Sub-almacenes (FE-F1-PR1). Bandejas paginadas,
 * detalle con sub-almacenes inline, y mutaciones crear/editar. Tras
 * cada mutación, invalida toda la familia <c>almacenKeys.all</c> para
 * que la bandeja refresque sin importar filtros activos.
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

// ─── Almacenes ────────────────────────────────────────────────────────────

export function useAlmacenes(filtros: ListarAlmacenesFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.almacenesList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/almacenes?${query}`
        : '/api/v1/almacen/almacenes';
      const { data } = await apiRequest<PagedResponse<AlmacenListItem>>(path, {
        signal,
      });
      return data;
    },
  });
}

export function useAlmacenById(id: string | undefined) {
  return useQuery({
    queryKey: id ? almacenKeys.almacenById(id) : ['almacen', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<AlmacenDetalle>(
        `/api/v1/almacen/almacenes/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export interface CrearAlmacenCommand {
  clave: string;
  nombre: string;
  sucursalId: string;
  estatus: number;
}

export interface CrearAlmacenResponse {
  id: string;
  clave: string;
}

export function useCrearAlmacen() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { command: CrearAlmacenCommand; idempotencyKey: string }) => {
      const { data } = await apiRequest<CrearAlmacenResponse>(
        '/api/v1/almacen/almacenes',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.almacenes() });
    },
  });
}

export interface EditarAlmacenCommand {
  id: string;
  clave: string;
  nombre: string;
  sucursalId: string;
  estatus: number;
}

export function useEditarAlmacen() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (command: EditarAlmacenCommand) => {
      await apiRequest<void>(`/api/v1/almacen/almacenes/${command.id}`, {
        method: 'PATCH',
        body: command,
      });
    },
    onSuccess: (_, command) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.almacenes() });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.almacenById(command.id),
      });
    },
  });
}

// ─── Sub-almacenes ────────────────────────────────────────────────────────

export function useSubAlmacenes(filtros: ListarSubAlmacenesFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.subAlmacenesList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/sub-almacenes?${query}`
        : '/api/v1/almacen/sub-almacenes';
      const { data } = await apiRequest<PagedResponse<SubAlmacenListItem>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export interface CrearSubAlmacenCommand {
  almacenId: string;
  clave: string;
  nombre: string;
  tipo: number;
  estatus: number;
}

export interface CrearSubAlmacenResponse {
  id: string;
  almacenId: string;
  clave: string;
}

export function useCrearSubAlmacen() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: CrearSubAlmacenCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CrearSubAlmacenResponse>(
        '/api/v1/almacen/sub-almacenes',
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

export interface EditarSubAlmacenCommand {
  id: string;
  clave: string;
  nombre: string;
  tipo: number;
  estatus: number;
}

export function useEditarSubAlmacen() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (command: EditarSubAlmacenCommand) => {
      await apiRequest<void>(`/api/v1/almacen/sub-almacenes/${command.id}`, {
        method: 'PATCH',
        body: command,
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.all });
    },
  });
}
