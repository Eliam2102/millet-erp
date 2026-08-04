import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { MotivoRechazoResponse } from '@/features/compras/api/types';

/**
 * <c>useMotivosRechazo()</c> — catálogo casi inmutable de motivos para
 * rechazar / eliminar / cancelar una RQ. Doc 05 §7.3 lo lista con
 * <c>staleTime: 1h</c> porque rara vez cambia durante una sesión.
 *
 * <para>El caller filtra por bitmask <c>aplicaA</c> según el flujo
 * (ej. modal de Rechazar muestra solo motivos con
 * <c>aplicaA &amp; Rechazo != 0</c>; ver
 * <c>aplicaABitmaskIncluye</c> en types.ts).</para>
 */
export function useMotivosRechazo() {
  return useQuery({
    queryKey: comprasKeys.motivosRechazo(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<MotivoRechazoResponse[]>(
        '/api/v1/compras/motivos-rechazo',
        { signal },
      );
      return data;
    },
    staleTime: 60 * 60 * 1000, // 1 h
    gcTime: 2 * 60 * 60 * 1000, // 2 h
  });
}
