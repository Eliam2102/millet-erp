import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type {
  ActualizarUnidadMedidaPayload,
  CrearUnidadMedidaPayload,
  UnidadMedidaResponse,
} from '@/modules/catalogos/api/types';

/**
 * Hooks del catálogo de unidades de medida (ADR-0046 Etapa 1a). El
 * <c>codigo</c> es inmutable. <c>dimension</c> y <c>factorABase</c> solo
 * deberían cambiarse antes de que la unidad esté en uso (el backend bloquea
 * con <c>UNIDAD_MEDIDA_EN_USO</c>; la fuente de "en uso" llega en Etapa 1b).
 */

/**
 * Fila de la tabla. El backend ya expone <c>estatus</c> (EstatusCatalogo),
 * que es justo lo que espera el <c>CatalogoEditableTable</c> genérico — se
 * usa directo, sin adapter.
 */
export type UnidadMedidaRow = UnidadMedidaResponse;

export function useUnidadesMedidaList() {
  return useQuery({
    queryKey: catalogosKeys.unidadesMedida(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<UnidadMedidaResponse[]>(
        '/api/v1/catalogos/unidades-medida',
        { signal },
      );
      return data;
    },
    staleTime: 30_000,
  });
}

export interface CrearUnidadMedidaArgs {
  payload: CrearUnidadMedidaPayload;
  idempotencyKey: string;
}

export function useCrearUnidadMedida() {
  const queryClient = useQueryClient();
  return useMutation<UnidadMedidaResponse, Error, CrearUnidadMedidaArgs>({
    mutationFn: async ({ payload, idempotencyKey }) => {
      const { data } = await apiRequest<UnidadMedidaResponse>(
        '/api/v1/catalogos/unidades-medida',
        { method: 'POST', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.unidadesMedida() });
    },
  });
}

export interface ActualizarUnidadMedidaArgs {
  id: string;
  payload: ActualizarUnidadMedidaPayload;
  idempotencyKey: string;
}

export function useActualizarUnidadMedida() {
  const queryClient = useQueryClient();
  return useMutation<UnidadMedidaResponse, Error, ActualizarUnidadMedidaArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<UnidadMedidaResponse>(
        `/api/v1/catalogos/unidades-medida/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.unidadesMedida() });
    },
  });
}

export interface DesactivarUnidadMedidaArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarUnidadMedida() {
  const queryClient = useQueryClient();
  return useMutation<UnidadMedidaResponse, Error, DesactivarUnidadMedidaArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<UnidadMedidaResponse>(
        `/api/v1/catalogos/unidades-medida/${id}/desactivar`,
        { method: 'POST', idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: catalogosKeys.unidadesMedida() });
    },
  });
}
