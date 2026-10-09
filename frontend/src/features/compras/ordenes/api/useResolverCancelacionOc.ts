import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from './keys';
import type { ResolverCancelacionOcValues } from '../schemas/cancelar-doble-firma';

export function useResolverCancelacionOc() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, command, idempotencyKey }: { id: string; command: ResolverCancelacionOcValues; idempotencyKey: string }) =>
      apiRequest<void>(`/api/v1/compras/ordenes/${id}/resolver-cancelacion`, { method: 'POST', body: command, idempotencyKey }),
    onSuccess: () => { void client.invalidateQueries({ queryKey: ordenesKeys.all }); },
  });
}
