import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { PagedResponse } from './types';
import { tesoreriaKeys } from './keys';

/** Espejo de AutorizacionPagoComercioExteriorResponse (I13.3). */
export type EstadoPagoComercioExterior =
  | 'PendienteNivel1'
  | 'PendienteNivel2'
  | 'Autorizado'
  | 'Rechazado';

export interface AutorizacionPagoComercioExteriorResponse {
  facturaProveedorId: string;
  tipo: 'Importacion' | 'Exportacion';
  estado: EstadoPagoComercioExterior;
  nivelPendiente: 1 | 2 | null;
  proveedorId: string;
  proveedorRazonSocial: string | null;
  folioProveedor: string | null;
  saldoPendiente: number;
  moneda: string;
  fechaVencimiento: string;
  nivel1UsuarioId: string | null;
  nivel1Nombre: string | null;
  nivel1En: string | null;
  nivel2UsuarioId: string | null;
  nivel2Nombre: string | null;
  nivel2En: string | null;
  motivoRechazo: string | null;
  puedoFirmar: boolean;
  puedoRechazar: boolean;
}

const BASE = '/api/v1/tesoreria/pagos-comercio-exterior';
const KEY = [...tesoreriaKeys.all, 'pagos-comercio-exterior'] as const;

export function usePagosComercioExterior(filtros: { estado?: string; soloMiNivel?: boolean; limit?: number } = {}) {
  return useQuery<PagedResponse<AutorizacionPagoComercioExteriorResponse>>({
    queryKey: [...KEY, filtros],
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado) params.set('estado', filtros.estado);
      if (filtros.soloMiNivel) params.set('soloMiNivel', 'true');
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<AutorizacionPagoComercioExteriorResponse>>(
        qs ? `${BASE}?${qs}` : BASE,
        { signal },
      );
      return data;
    },
  });
}

function useAccionPagoCe<T extends { facturaProveedorId: string; idempotencyKey: string }>(
  accion: (a: T) => { path: string; body?: unknown },
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: T) => {
      const { path, body } = accion(args);
      const { data } = await apiRequest<AutorizacionPagoComercioExteriorResponse>(
        `${BASE}/${args.facturaProveedorId}${path}`,
        { method: 'POST', body, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    // La firma completa cambia también los pasivos seleccionables de la corrida.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all }),
  });
}

/** Nivel 1 (Compras de MP / Documentación de Exportación) o nivel 2 (Dirección); la misma persona no firma los dos. */
export function useAutorizarPagoComercioExterior() {
  return useAccionPagoCe((a: { facturaProveedorId: string; nivel: 1 | 2; idempotencyKey: string }) => ({
    path: `/autorizar/${a.nivel}`,
  }));
}

export function useRechazarPagoComercioExterior() {
  return useAccionPagoCe((a: { facturaProveedorId: string; motivo: string; idempotencyKey: string }) => ({
    path: '/rechazar',
    body: { motivo: a.motivo },
  }));
}
