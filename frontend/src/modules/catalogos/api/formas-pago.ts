import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';
import type { FormaPagoItem } from '@/modules/catalogos/api/types';

/**
 * Hook del catálogo SAT Formas de Pago (UF-Admin-PR5.3). con habilitación administrativa,
 * mantenido vía migración del backend (catálogo SAT). Sin paginación.
 */
export function useFormasPago(administracion = false) {
  return useQuery({
    queryKey: [...catalogosKeys.formasPago(), administracion],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<FormaPagoItem[]>(
        `/api/v1/catalogos/formas-pago${administracion ? '/administracion' : ''}`,
        { signal },
      );
      return data;
    },
    staleTime: 0, // El estado puede cambiar durante la sesión.
    gcTime: 2 * 60 * 60 * 1000,
  });
}

export function useCambiarEstadoFormaPago() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      activa,
      idempotencyKey,
    }: {
      id: string;
      activa: boolean;
      idempotencyKey: string;
    }) =>
      apiRequest<void>(`/api/v1/catalogos/formas-pago/${id}/estado`, {
        method: 'PATCH',
        body: { activa },
        idempotencyKey,
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: catalogosKeys.formasPago() }),
  });
}
