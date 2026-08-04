import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type {
  PagedResponse,
  RegistrarSeguimientoCobranzaCommand,
  SeguimientoCobranzaResponse,
} from '@/features/cxc/api/types';

const BASE = '/api/v1/cuentas-por-cobrar/cobranza';

export interface ListarCobranzaFiltros {
  /** Obligatorio en el backend — la bitácora siempre es POR cliente. */
  clienteId: string;
  resultado?: number;
  offset?: number;
  limit?: number;
}

/**
 * <c>useSeguimientosCobranza(filtros)</c> — bitácora append-only por
 * cliente (CXC-PR5). Lectura con <c>cartera.leer</c>. El filtro de
 * cliente es OBLIGATORIO (05-frontend-diseno §2); la query no se
 * dispara sin él.
 */
export function useSeguimientosCobranza(
  filtros: Omit<ListarCobranzaFiltros, 'clienteId'> & { clienteId: string | null },
) {
  return useQuery<PagedResponse<SeguimientoCobranzaResponse>>({
    queryKey: cxcKeys.cobranzaList({
      clienteId: filtros.clienteId ?? null,
      resultado: filtros.resultado ?? null,
      offset: filtros.offset ?? 0,
      limit: filtros.limit ?? 100,
    }),
    enabled: filtros.clienteId != null,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      params.set('clienteId', filtros.clienteId!);
      if (filtros.resultado != null)
        params.set('resultado', String(filtros.resultado));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const { data } = await apiRequest<PagedResponse<SeguimientoCobranzaResponse>>(
        `${BASE}?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useRegistrarSeguimientoCobranza()</c> — POST append-only con
 * Idempotency-Key. Promesa de pago exige monto y fecha (espejo
 * <c>SC_PROMESA_SIN_MONTO</c>/<c>SC_PROMESA_SIN_FECHA</c>).
 */
export function useRegistrarSeguimientoCobranza() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarSeguimientoCobranzaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<SeguimientoCobranzaResponse>(BASE, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.cobranza() });
    },
  });
}
