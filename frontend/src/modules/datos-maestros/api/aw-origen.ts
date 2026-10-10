import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

export type AwOrigen = 'Real' | 'Demo';
export interface AwOrigenEstado {
  origen: AwOrigen;
  permitido: boolean;
  demoConfigurada: boolean;
  origenClientesReal: string;
  origenProductosReal: string;
  cambiadoPor: string | null;
  cambiadoEn: string | null;
  version: number;
}
const key = ['integraciones-aw', 'origen'] as const;
const path = '/api/v1/integraciones/aw/origen';

export function useAwOrigen(enabled: boolean) {
  return useQuery({
    queryKey: key,
    queryFn: async ({ signal }) => (await apiRequest<AwOrigenEstado>(path, { signal })).data,
    enabled,
    staleTime: 0,
    refetchInterval: 15_000,
  });
}

export function useCambiarAwOrigen() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ origen, version, idempotencyKey }: {
      origen: AwOrigen; version: number; idempotencyKey: string;
    }) => (await apiRequest<AwOrigenEstado>(path, {
      method: 'PUT', body: { origen }, ifMatch: String(version), idempotencyKey,
    })).data,
    onSuccess: (estado) => queryClient.setQueryData(key, estado),
    onSettled: () => queryClient.invalidateQueries({ queryKey: key }),
  });
}

export function etiquetaAwOrigen(estado: AwOrigenEstado, area: 'clientes' | 'productos'): string {
  if (estado.origen === 'Demo') return 'Origen: copia de demo';
  const real = area === 'clientes' ? estado.origenClientesReal : estado.origenProductosReal;
  return real === 'Simulado' ? 'Origen: simulado' : 'Origen: A+W en vivo';
}
export function puedeCambiarAwOrigen(estado: AwOrigenEstado, permisos: readonly string[]): boolean {
  return estado.permitido && permisos.includes('integraciones.aw.administracion.configuracion');
}
