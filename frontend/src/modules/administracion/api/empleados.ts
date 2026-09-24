import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ActualizarEmpleadoPayload,
  CrearEmpleadoCommand,
  EmpleadoResponse,
} from '@/modules/administracion/api/types';
import type {
  EmpleadoListItem,
  PagedCatalogoResponse,
} from '@/features/catalogos/api';

/**
 * Query + mutations del master de Empleados (ADM-FE-PR1, backend
 * ADM-PR1 en <c>compartido.empleados</c>). Lectura por
 * <c>GET /catalogos/empleados</c> (incluye inactivos para el admin);
 * mutaciones en <c>/api/v1/admin/empleados</c> con permiso
 * <c>admin.empleados.gestionar</c>.
 *
 * <para>Tras cualquier mutación se invalida la familia
 * <c>adminKeys.empleados()</c> Y el cache eager de los selectores
 * (<c>['catalogos','empleados']</c> — <c>EmpleadoSelector</c> de
 * viáticos/comprobaciones).</para>
 */
const BASE = '/api/v1/admin/empleados';

/** Clave del cache eager de features/catalogos (EmpleadoSelector). */
const CATALOGOS_EMPLEADOS_KEY = ['catalogos', 'empleados'];

export function useEmpleadosAdmin() {
  return useQuery<EmpleadoListItem[]>({
    queryKey: adminKeys.empleadosList(),
    queryFn: async ({ signal }) => {
      const items: EmpleadoListItem[] = [];
      let offset = 0;
      for (;;) {
        const { data } = await apiRequest<PagedCatalogoResponse<EmpleadoListItem>>(
          `/api/v1/catalogos/empleados?limit=200&offset=${offset}`,
          { signal },
        );
        items.push(...data.items);
        offset += data.items.length;
        if (data.items.length === 0 || offset >= (data.total ?? items.length)) break;
      }
      return items;
    },
  });
}

export interface CrearEmpleadoArgs {
  command: CrearEmpleadoCommand;
  idempotencyKey: string;
}

export function useCrearEmpleado() {
  const queryClient = useQueryClient();
  return useMutation<EmpleadoResponse, Error, CrearEmpleadoArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<EmpleadoResponse>(BASE, {
        method: 'POST',
        body: command,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => invalidar(queryClient),
  });
}

export interface AltaColaboradorCommand {
  id: string;
  clave: string;
  nombre: string;
  sucursalId: string;
  departamentoId: string;
  puestoId: string;
  acceso: 0 | 1 | 2;
  correoCorporativo: string | null;
  emailContacto: string | null;
  rolId: string | null;
  jefeDirectoId: string | null;
  codigoNomina: string | null;
}

export function useAltaColaborador() {
  const queryClient = useQueryClient();
  return useMutation<
    { empleado: EmpleadoResponse; acceso: { estadoAcceso: number } | null },
    Error,
    { command: AltaColaboradorCommand; idempotencyKey: string }
  >({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<{
        empleado: EmpleadoResponse;
        acceso: { estadoAcceso: number } | null;
      }>('/api/v1/admin/colaboradores', {
        method: 'POST',
        body: command,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      invalidar(queryClient);
      queryClient.invalidateQueries({ queryKey: ['identidad', 'usuarios'] });
    },
  });
}

export interface ActualizarEmpleadoArgs {
  id: string;
  payload: ActualizarEmpleadoPayload;
  idempotencyKey: string;
}

export function useActualizarEmpleado() {
  const queryClient = useQueryClient();
  return useMutation<EmpleadoResponse, Error, ActualizarEmpleadoArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<EmpleadoResponse>(`${BASE}/${id}`, {
        method: 'PATCH',
        body: payload,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => invalidar(queryClient),
  });
}

export interface CambiarEstatusEmpleadoArgs {
  id: string;
  idempotencyKey: string;
}

export function useDesactivarEmpleado() {
  return useCambioEstatusEmpleado('desactivar');
}

export function useReactivarEmpleado() {
  return useCambioEstatusEmpleado('reactivar');
}

function useCambioEstatusEmpleado(accion: 'desactivar' | 'reactivar') {
  const queryClient = useQueryClient();
  return useMutation<EmpleadoResponse, Error, CambiarEstatusEmpleadoArgs>({
    mutationFn: async ({ id, idempotencyKey }) => {
      const { data } = await apiRequest<EmpleadoResponse>(
        `${BASE}/${id}/${accion}`,
        { method: 'POST', body: {}, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => invalidar(queryClient),
  });
}

function invalidar(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: adminKeys.empleados() });
  queryClient.invalidateQueries({ queryKey: CATALOGOS_EMPLEADOS_KEY });
}
