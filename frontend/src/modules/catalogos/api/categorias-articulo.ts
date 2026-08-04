import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type {
  ActualizarCategoriaArticuloPayload,
  CategoriaArticuloResponse,
  CrearCategoriaArticuloPayload,
} from '@/modules/catalogos/api/types';

/**
 * Hooks del catálogo Categorías de Artículo (patrón ADR-0046). Reemplaza el
 * string libre <c>Articulo.Categoria</c>; el FK <c>categoria_id</c> llega en
 * PR2. Mutación con el permiso grueso <c>compartido.catalogos.administrar</c>
 * (molde UsoPrincipal). GET read-only con <c>compartido.catalogos.leer</c>.
 * Desactivar = <c>POST /{id}/desactivar</c> (soft-delete).
 */

export function useCategoriasArticuloList() {
  return useQuery({
    queryKey: catalogosKeys.categoriasArticulo(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CategoriaArticuloResponse[]>(
        '/api/v1/catalogos/categorias-articulo',
        { signal },
      );
      return data;
    },
    staleTime: 30_000,
  });
}

export interface CrearCategoriaArticuloArgs {
  payload: CrearCategoriaArticuloPayload;
  idempotencyKey: string;
}

export function useCrearCategoriaArticulo() {
  const queryClient = useQueryClient();
  return useMutation<CategoriaArticuloResponse, Error, CrearCategoriaArticuloArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<CategoriaArticuloResponse>(
        '/api/v1/catalogos/categorias-articulo',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.categoriasArticulo(),
      });
    },
  });
}

export interface ActualizarCategoriaArticuloArgs {
  id: string;
  payload: ActualizarCategoriaArticuloPayload;
  idempotencyKey: string;
}

export function useActualizarCategoriaArticulo() {
  const queryClient = useQueryClient();
  return useMutation<
    CategoriaArticuloResponse,
    Error,
    ActualizarCategoriaArticuloArgs
  >({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<CategoriaArticuloResponse>(
        `/api/v1/catalogos/categorias-articulo/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.categoriasArticulo(),
      });
    },
  });
}

export interface DesactivarCategoriaArticuloArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarCategoriaArticulo() {
  const queryClient = useQueryClient();
  return useMutation<
    CategoriaArticuloResponse,
    Error,
    DesactivarCategoriaArticuloArgs
  >({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<CategoriaArticuloResponse>(
        `/api/v1/catalogos/categorias-articulo/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: catalogosKeys.categoriasArticulo(),
      });
    },
  });
}
