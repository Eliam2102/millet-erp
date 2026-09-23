import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ActualizarPuestoPayload,
  CrearPuestoCommand,
  PuestoResponse,
} from '@/modules/administracion/api/types';
import type {
  PagedCatalogoResponse,
  PuestoListItem,
} from '@/features/catalogos/api';

/**
 * Query + mutations del catálogo de Puestos (ADM-FE-PR1, backend
 * ADM-PR1 en <c>compartido.puestos</c>). Lectura por
 * <c>GET /catalogos/puestos</c> (incluye inactivos para el admin);
 * mutaciones en <c>/api/v1/admin/puestos</c> con permiso
 * <c>admin.puestos.gestionar</c>.
 *
 * <para>Tras cualquier mutación se invalida la familia
 * <c>adminKeys.puestos()</c> Y el cache eager de los selectores
 * (<c>['catalogos','puestos']</c> — <c>PuestoSelector</c> y la política
 * de viáticos de CxP).</para>
 */
const BASE = '/api/v1/admin/puestos';

/** Clave del cache eager de features/catalogos (PuestoSelector). */
const CATALOGOS_PUESTOS_KEY = ['catalogos', 'puestos'];

export function usePuestosAdmin() {
  return useQuery<PuestoListItem[]>({
    queryKey: adminKeys.puestosList(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<PagedCatalogoResponse<PuestoListItem>>(
        `${BASE}?limit=200`,
        { signal },
      );
      return data.items;
    },
  });
}

export interface CrearPuestoArgs {
  command: CrearPuestoCommand;
  idempotencyKey: string;
}

export function useCrearPuesto() {
  const queryClient = useQueryClient();
  return useMutation<PuestoResponse, Error, CrearPuestoArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<PuestoResponse>(BASE, {
        method: 'POST',
        body: command,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => invalidar(queryClient),
  });
}

export interface ActualizarPuestoArgs {
  id: string;
  payload: ActualizarPuestoPayload;
  idempotencyKey: string;
}

export function useActualizarPuesto() {
  const queryClient = useQueryClient();
  return useMutation<PuestoResponse, Error, ActualizarPuestoArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<PuestoResponse>(`${BASE}/${id}`, {
        method: 'PATCH',
        body: payload,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => invalidar(queryClient),
  });
}

export interface CambiarEstatusPuestoArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarPuesto() {
  return useCambioEstatusPuesto('desactivar');
}

export function useReactivarPuesto() {
  return useCambioEstatusPuesto('reactivar');
}

function useCambioEstatusPuesto(accion: 'desactivar' | 'reactivar') {
  const queryClient = useQueryClient();
  return useMutation<PuestoResponse, Error, CambiarEstatusPuestoArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<PuestoResponse>(
        `${BASE}/${id}/${accion}`,
        { method: 'POST', body: {}, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => invalidar(queryClient),
  });
}

function invalidar(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: adminKeys.puestos() });
  queryClient.invalidateQueries({ queryKey: CATALOGOS_PUESTOS_KEY });
}
