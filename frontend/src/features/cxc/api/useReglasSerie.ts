import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type {
  ComportamientoSerie,
  HistorialReglaSerie,
  ReglaLiberacionSerieAdmin,
} from '@/features/cxc/api/types';

/**
 * C7.2 (86e3kdxy6): administración de las reglas de liberación por serie.
 * Permiso <c>cuentas_por_cobrar.reglas-serie.gestionar</c>. Cada cambio
 * invalida también las reglas activas que lee el panel de decidir.
 */
const BASE = '/api/v1/cuentas-por-cobrar/liberaciones/reglas-serie/admin';

export function useReglasSerieAdmin() {
  return useQuery<ReglaLiberacionSerieAdmin[]>({
    queryKey: cxcKeys.reglasSerieAdmin(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ReglaLiberacionSerieAdmin[]>(BASE, { signal });
      return data;
    },
  });
}

export function useHistorialReglasSerie() {
  return useQuery<HistorialReglaSerie[]>({
    queryKey: cxcKeys.reglasSerieHistorial(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<HistorialReglaSerie[]>(`${BASE}/historial`, { signal });
      return data;
    },
  });
}

export function useCrearReglaSerie() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      prefijo: string;
      comportamiento: ComportamientoSerie;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ReglaLiberacionSerieAdmin>(BASE, {
        method: 'POST',
        body: { prefijo: args.prefijo, comportamiento: args.comportamiento },
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: cxcKeys.reglasSerie() }),
  });
}

export function useCambiarComportamientoSerie() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      comportamiento: ComportamientoSerie;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ReglaLiberacionSerieAdmin>(
        `${BASE}/${args.id}/comportamiento`,
        {
          method: 'PUT',
          body: { comportamiento: args.comportamiento },
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: cxcKeys.reglasSerie() }),
  });
}

export function useCambiarEstadoReglaSerie() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      activo: boolean;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ReglaLiberacionSerieAdmin>(`${BASE}/${args.id}/estado`, {
        method: 'PUT',
        body: { activo: args.activo },
        idempotencyKey: args.idempotencyKey,
        headers: { 'X-Expected-Version': String(args.versionEsperada) },
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: cxcKeys.reglasSerie() }),
  });
}
