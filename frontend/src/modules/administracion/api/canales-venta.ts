import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { adminKeys } from '@/modules/administracion/api/keys';
import type {
  ActualizarCanalVentaPayload,
  CanalVentaResponse,
  CrearCanalVentaCommand,
} from '@/modules/administracion/api/types';

/**
 * Query + mutations del catálogo de Canales de venta (FAC-ING-PR3,
 * backend FAC-ING-PR2). CRUD en <c>/api/v1/admin/canales-venta</c> con
 * el mismo permiso que Sucursales (<c>admin.empresas.sucursales-gestionar</c>).
 *
 * <para>Tras cualquier mutación se invalida la familia
 * <c>adminKeys.canalesVenta()</c> Y el lookup que consumen los selectores
 * de Facturación (<c>GET /facturacion/catalogos/canales-venta</c>, cache
 * bajo <c>['facturacion', 'catalogos', 'canales-venta']</c>).</para>
 */
const BASE = '/api/v1/admin/canales-venta';

/** Clave laxa del lookup de Facturación (ver facturacionKeys.canalesVentaLookup). */
const FACTURACION_LOOKUP_KEY = ['facturacion', 'catalogos', 'canales-venta'];

export function useCanalesVentaAdmin(estatus?: number | null) {
  return useQuery<CanalVentaResponse[]>({
    queryKey: adminKeys.canalesVentaList(estatus),
    queryFn: async ({ signal }) => {
      const qs = estatus != null ? `?estatus=${estatus}` : '';
      const { data } = await apiRequest<CanalVentaResponse[]>(
        `${BASE}${qs}`,
        { signal },
      );
      return data;
    },
  });
}

export interface CrearCanalVentaArgs {
  command: CrearCanalVentaCommand;
  idempotencyKey: string;
}

export function useCrearCanalVenta() {
  const queryClient = useQueryClient();
  return useMutation<CanalVentaResponse, Error, CrearCanalVentaArgs>({
    mutationFn: async ({ command, idempotencyKey }) => {
      const { data } = await apiRequest<CanalVentaResponse>(BASE, {
        method: 'POST',
        body: command,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminKeys.canalesVenta() });
      queryClient.invalidateQueries({ queryKey: FACTURACION_LOOKUP_KEY });
    },
  });
}

export interface ActualizarCanalVentaArgs {
  id: number;
  payload: ActualizarCanalVentaPayload;
  idempotencyKey: string;
}

export function useActualizarCanalVenta() {
  const queryClient = useQueryClient();
  return useMutation<CanalVentaResponse, Error, ActualizarCanalVentaArgs>({
    mutationFn: async ({ id, payload, idempotencyKey }) => {
      const { data } = await apiRequest<CanalVentaResponse>(`${BASE}/${id}`, {
        method: 'PATCH',
        body: payload,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminKeys.canalesVenta() });
      queryClient.invalidateQueries({ queryKey: FACTURACION_LOOKUP_KEY });
    },
  });
}
