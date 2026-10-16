import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { NivelAutorizacion } from '@/features/compras/api/types';

/** Resultado por id de la autorización masiva (S2). */
export interface AutorizacionMasivaItemResultado {
  id: string;
  ok: boolean;
  errorCodigo: string | null;
  mensaje: string | null;
}

export interface AutorizacionMasivaResponse {
  resultados: AutorizacionMasivaItemResultado[];
}

export interface AutorizarMasivoArgs {
  /** Entre 1 y 50 requisiciones, todas del mismo nivel pendiente. */
  ids: readonly string[];
  nivel: NivelAutorizacion;
  notas?: string | null;
  idempotencyKey: string;
}

/**
 * <c>useAutorizarMasivo()</c> — <c>POST
 * /api/v1/compras/requisiciones/autorizaciones-masivas</c> (S2).
 *
 * <para>El backend responde 200 con un resultado por id; un id fallido no
 * anula el lote. La UI debe leer <c>resultados</c>, no solo el éxito HTTP.</para>
 *
 * <para>Idempotency-Key obligatorio (ADR-0020). Tras la respuesta se
 * invalida la familia de compras para refrescar la bandeja.</para>
 */
export function useAutorizarMasivo() {
  const queryClient = useQueryClient();
  return useMutation<AutorizacionMasivaResponse, Error, AutorizarMasivoArgs>({
    mutationFn: async ({ ids, nivel, notas, idempotencyKey }) => {
      const { data } = await apiRequest<AutorizacionMasivaResponse>(
        '/api/v1/compras/requisiciones/autorizaciones-masivas',
        {
          method: 'POST',
          body: { ids, nivel, notas: notas ?? null },
          idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: comprasKeys.all }),
  });
}
