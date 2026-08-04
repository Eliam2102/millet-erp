import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ActualizarDepartamentoPayload,
  CrearDepartamentoCommand,
  DepartamentoResponse,
} from '@/modules/administracion/api/types';

/**
 * Mutations de Departamentos (F-Admin-PR2.3 backend). Reading sigue
 * pasando por el detalle de empresa o por el catálogo público
 * (<c>GET /api/v1/catalogos/departamentos</c>).
 *
 * <para>El backend solo expone POST y PATCH (no hay desactivar — el
 * MVP no lo necesitó). Si más adelante se requiere, se agrega un hook
 * aquí espejando el endpoint.</para>
 */

export interface CrearDepartamentoArgs {
  empresaId: string;
  command: CrearDepartamentoCommand;
  idempotencyKey: string;
}

export function useCrearDepartamento() {
  const queryClient = useQueryClient();
  return useMutation<DepartamentoResponse, Error, CrearDepartamentoArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<DepartamentoResponse>(
        '/api/v1/admin/departamentos',
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.empresa(vars.empresaId),
      });
      queryClient.invalidateQueries({
        queryKey: ['catalogos', 'departamentos'],
      });
    },
  });
}

export interface ActualizarDepartamentoArgs {
  empresaId: string;
  id: string;
  payload: ActualizarDepartamentoPayload;
  idempotencyKey: string;
}

export function useActualizarDepartamento() {
  const queryClient = useQueryClient();
  return useMutation<DepartamentoResponse, Error, ActualizarDepartamentoArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<DepartamentoResponse>(
        `/api/v1/admin/departamentos/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: adminKeys.empresa(vars.empresaId),
      });
      queryClient.invalidateQueries({
        queryKey: ['catalogos', 'departamentos'],
      });
    },
  });
}
