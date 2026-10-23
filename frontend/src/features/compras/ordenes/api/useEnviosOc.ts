import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from './keys';

export interface EnvioOc {
  id: string;
  destinatario: string;
  usuarioId: string;
  usuarioNombre: string;
  fecha: string;
  modo: 'correo' | 'manual';
  resultado: 'pendiente' | 'registrado' | 'simulado' | 'aceptado' | 'fallido';
}
export interface EnviosOc {
  correoProveedor: string | null;
  esSimulado: boolean;
  puedeEnviar: boolean;
  motivoBloqueo: string | null;
  envios: EnvioOc[];
}
export function useEnviosOc(id: string) {
  return useQuery({
    queryKey: [...ordenesKeys.detail(id), 'envios'],
    queryFn: async ({ signal }) =>
      (await apiRequest<EnviosOc>(`/api/v1/compras/ordenes/${id}/envios`, { signal })).data,
  });
}
export function useRegistrarEnvioOc(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({
      destinatario,
      modo,
      idempotencyKey,
    }: {
      destinatario: string;
      modo: 'correo' | 'manual';
      idempotencyKey: string;
    }) =>
      (
        await apiRequest<EnvioOc>(`/api/v1/compras/ordenes/${id}/envios`, {
          method: 'POST',
          body: { destinatario, modo },
          idempotencyKey,
        })
      ).data,
    onSuccess: () => client.invalidateQueries({ queryKey: ordenesKeys.detail(id) }),
  });
}
