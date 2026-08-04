import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  ActualizarOperadorPayload,
  ActualizarVehiculoPayload,
  CatalogoCreadoResponse,
  CrearOperadorCommand,
  CrearVehiculoCommand,
  OperadorListItem,
  VehiculoListItem,
} from '@/features/facturacion/api/types';

/**
 * Queries + mutations de los catálogos de Carta Porte (vehículos y
 * operadores) — cierra PLATFORM-TODO(&lt;VehiculoPicker&gt;/&lt;OperadorPicker&gt;).
 * CRUD en <c>/api/v1/facturacion/carta-porte/{vehiculos|operadores}</c>:
 * GET con <c>?incluirInactivos=</c> (permiso <c>carta-porte.leer</c>),
 * POST/PATCH con Idempotency-Key (permiso <c>carta-porte.emitir</c>).
 *
 * <para>Tras cualquier mutación se invalida la familia raíz del catálogo
 * (<c>facturacionKeys.cartaPorteVehiculos()</c> /
 * <c>cartaPorteOperadores()</c>), que cubre la página de admin y los
 * pickers de emisión.</para>
 */
const BASE = '/api/v1/facturacion/carta-porte';

export function useVehiculosAdmin(incluirInactivos = false) {
  return useQuery<VehiculoListItem[]>({
    queryKey: facturacionKeys.cartaPorteVehiculosList(incluirInactivos),
    queryFn: async ({ signal }) => {
      const qs = incluirInactivos ? '?incluirInactivos=true' : '';
      const { data } = await apiRequest<VehiculoListItem[]>(
        `${BASE}/vehiculos${qs}`,
        { signal },
      );
      return data;
    },
  });
}

export function useOperadoresAdmin(incluirInactivos = false) {
  return useQuery<OperadorListItem[]>({
    queryKey: facturacionKeys.cartaPorteOperadoresList(incluirInactivos),
    queryFn: async ({ signal }) => {
      const qs = incluirInactivos ? '?incluirInactivos=true' : '';
      const { data } = await apiRequest<OperadorListItem[]>(
        `${BASE}/operadores${qs}`,
        { signal },
      );
      return data;
    },
  });
}

export interface CrearVehiculoArgs {
  command: CrearVehiculoCommand;
  idempotencyKey: string;
}

export function useCrearVehiculo() {
  const queryClient = useQueryClient();
  return useMutation<CatalogoCreadoResponse, Error, CrearVehiculoArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<CatalogoCreadoResponse>(
        `${BASE}/vehiculos`,
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.cartaPorteVehiculos(),
      });
    },
  });
}

export interface ActualizarVehiculoArgs {
  id: string;
  payload: ActualizarVehiculoPayload;
  idempotencyKey: string;
}

export function useActualizarVehiculo() {
  const queryClient = useQueryClient();
  return useMutation<VehiculoListItem, Error, ActualizarVehiculoArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<VehiculoListItem>(
        `${BASE}/vehiculos/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.cartaPorteVehiculos(),
      });
    },
  });
}

export interface CrearOperadorArgs {
  command: CrearOperadorCommand;
  idempotencyKey: string;
}

export function useCrearOperador() {
  const queryClient = useQueryClient();
  return useMutation<CatalogoCreadoResponse, Error, CrearOperadorArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<CatalogoCreadoResponse>(
        `${BASE}/operadores`,
        { method: 'POST', body: command, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.cartaPorteOperadores(),
      });
    },
  });
}

export interface ActualizarOperadorArgs {
  id: string;
  payload: ActualizarOperadorPayload;
  idempotencyKey: string;
}

export function useActualizarOperador() {
  const queryClient = useQueryClient();
  return useMutation<OperadorListItem, Error, ActualizarOperadorArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<OperadorListItem>(
        `${BASE}/operadores/${id}`,
        { method: 'PATCH', body: payload, idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.cartaPorteOperadores(),
      });
    },
  });
}
