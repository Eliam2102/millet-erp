import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import { useAuthStore } from '@/lib/auth/auth-store';
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
  const sucursalId = useAuthStore((s) => s.currentSucursalId);
  return useQuery<EmpleadoListItem[]>({
    queryKey: adminKeys.empleadosList(sucursalId),
    queryFn: async ({ signal }) => {
      const items: EmpleadoListItem[] = [];
      let offset = 0;
      for (;;) {
        const { data } = await apiRequest<PagedCatalogoResponse<EmpleadoListItem>>(
          `/api/v1/catalogos/empleados?limit=200&offset=${offset}${sucursalId ? `&sucursalId=${encodeURIComponent(sucursalId)}` : ''}`,
          { signal },
        );
        items.push(...data.items);
        offset += data.items.length;
        if (data.items.length === 0 || offset >= (data.total ?? items.length)) break;
      }
      return items;
    },
    refetchInterval: (query) =>
      query.state.data?.some((e) => e.estadoAcceso === 2) ? 3000 : false,
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
  clave?: string | null;
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

export interface SiguienteClaveEmpleadoResponse {
  siguienteClave: string;
}

export function useSiguienteClaveEmpleado(enabled = true) {
  return useQuery<SiguienteClaveEmpleadoResponse>({
    queryKey: ['admin', 'empleados', 'siguiente-clave'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<SiguienteClaveEmpleadoResponse>(
        `${BASE}/siguiente-clave`,
        { signal },
      );
      return data;
    },
    enabled,
    staleTime: 5000,
  });
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

export interface AccesoColaboradorEstado {
  empleadoId: string;
  usuarioId: string;
  email: string;
  estadoAcceso: 0 | 1 | 2 | 3;
  motivoErrorProvision: string | null;
  accesoEnviadoEn: string | null;
  primerAccesoEn: string | null;
  emailContacto: string | null;
  usuarioActivo: boolean;
}

export interface ValidacionCorreoCorporativo {
  correo: string;
  dominioPermitido: boolean;
  cuentaEntra: { objectId: string; nombreMostrado: string; habilitada: boolean } | null;
  usuarioErp: { id: string; nombre: string; estadoAcceso: number; activo: boolean } | null;
  empleadoVinculado?: { id: string; clave: string; nombre: string } | null;
  puedeVincularCuentaExistente: boolean;
  puedeCrearCuentaNueva: boolean;
  motivoBloqueo?: string | null;
}

export function useValidarCorreoCorporativo(correo: string, enabled: boolean) {
  return useQuery<ValidacionCorreoCorporativo>({
    queryKey: ['identidad', 'directorio-entra', correo],
    enabled: enabled && /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(correo),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ValidacionCorreoCorporativo>(
        `/api/v1/identidad/directorio-entra/validar-correo?correo=${encodeURIComponent(correo)}`,
        { signal },
      );
      return data;
    },
    staleTime: 10_000,
  });
}

export function useAccesoColaborador(empleadoId: string | null) {
  return useQuery<AccesoColaboradorEstado>({
    queryKey: ['admin', 'colaboradores', empleadoId, 'acceso'],
    enabled: empleadoId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<AccesoColaboradorEstado>(
        `/api/v1/admin/colaboradores/${empleadoId}/acceso`, { signal },
      );
      return data;
    },
    refetchInterval: (query) =>
      query.state.data?.estadoAcceso === 2 ? 3000 : false,
  });
}

export function useDarAccesoColaborador() {
  const queryClient = useQueryClient();
  return useMutation<AltaColaboradorResponse, Error, {
    empleadoId: string;
    acceso: 1 | 2;
    correoCorporativo: string;
    emailContacto: string | null;
    rolId: string;
    idempotencyKey: string;
  }>({
    mutationFn: async ({ empleadoId, idempotencyKey, ...body }) => {
      const { data } = await apiRequest<AltaColaboradorResponse>(
        `/api/v1/admin/colaboradores/${empleadoId}/acceso`,
        { method: 'POST', body, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      invalidar(queryClient);
      queryClient.invalidateQueries({ queryKey: ['identidad', 'usuarios'] });
      queryClient.invalidateQueries({ queryKey: ['admin', 'colaboradores', vars.empleadoId, 'acceso'] });
    },
  });
}

interface AltaColaboradorResponse {
  empleado: EmpleadoResponse;
  acceso: { usuarioId: string; estadoAcceso: number } | null;
}

export function useAccionAccesoColaborador(accion: 'reintentar' | 'reenviar') {
  const queryClient = useQueryClient();
  return useMutation<AccesoColaboradorEstado, Error, { empleadoId: string; idempotencyKey: string }>({
    mutationFn: async ({ empleadoId, idempotencyKey }) => {
      const { data } = await apiRequest<AccesoColaboradorEstado>(
        `/api/v1/admin/colaboradores/${empleadoId}/acceso/${accion}`,
        { method: 'POST', body: {}, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      invalidar(queryClient);
      queryClient.invalidateQueries({
        queryKey: ['admin', 'colaboradores', vars.empleadoId, 'acceso'],
      });
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
  queryClient.invalidateQueries({ queryKey: ['admin', 'empleados', 'siguiente-clave'] });
  queryClient.invalidateQueries({ queryKey: ['identidad', 'usuarios'] });
  queryClient.invalidateQueries({ queryKey: ['admin', 'colaboradores'] });
}
