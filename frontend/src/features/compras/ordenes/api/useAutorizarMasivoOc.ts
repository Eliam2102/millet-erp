import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { NivelAutorizacion } from '@/features/compras/ordenes/schemas/autorizar';

/** Resultado por id de la autorización masiva de OC. */
export interface AutorizacionMasivaOcItemResultado {
  id: string;
  ok: boolean;
  errorCodigo: string | null;
  mensaje: string | null;
}

export interface AutorizacionMasivaOcResponse {
  resultados: AutorizacionMasivaOcItemResultado[];
}

export interface AutorizarMasivoOcArgs {
  /** Entre 1 y 50 órdenes de compra, todas del mismo nivel pendiente. */
  ids: readonly string[];
  nivel: NivelAutorizacion;
  notas?: string | null;
  idempotencyKey: string;
}

/**
 * <c>useAutorizarMasivoOc()</c> — <c>POST
 * /api/v1/compras/ordenes/autorizaciones-masivas</c>.
 *
 * <para>El backend responde 200 con un resultado por id; un id fallido no
 * anula el lote. La UI debe leer <c>resultados</c>, no solo el éxito HTTP.</para>
 *
 * <para>Idempotency-Key obligatorio (ADR-0020). Tras la respuesta se
 * invalida la familia de OC para refrescar la bandeja.</para>
 */
export function useAutorizarMasivoOc() {
  const queryClient = useQueryClient();
  return useMutation<AutorizacionMasivaOcResponse, Error, AutorizarMasivoOcArgs>({
    mutationFn: async ({ ids, nivel, notas, idempotencyKey }) => {
      const { data } = await apiRequest<AutorizacionMasivaOcResponse>(
        '/api/v1/compras/ordenes/autorizaciones-masivas',
        {
          method: 'POST',
          body: { ids, nivel, notas: notas ?? null },
          idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ordenesKeys.all }),
  });
}
