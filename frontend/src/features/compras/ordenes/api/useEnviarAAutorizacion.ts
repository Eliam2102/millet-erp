import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

export interface EnviarAAutorizacionMutationArgs {
  ordenCompraId: string;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useEnviarAAutorizacion()</c> — POST <c>/{id}/transmitir</c>
 * (UF4-PR1). Transición <c>Borrador</c>/<c>Rechazada</c> →
 * <c>EnAutorizacionJefeCompras</c>. Backend valida invariantes
 * pre-autorización (≥1 línea, proveedor activo, cotización adjunta o
 * excepción, ficha técnica si importación, motivo + correo si sin RQ).
 *
 * <para>onSuccess invalida el detalle + bandejas (la OC migra de
 * "borradores" a "pendientes").</para>
 */
export function useEnviarAAutorizacion() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, EnviarAAutorizacionMutationArgs>({
    mutationFn: async ({ ordenCompraId, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/transmitir`,
        {
          method: 'POST',
          idempotencyKey,
        },
      );
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(ordenCompraId),
      });
      // Invalidar listas (bandeja general + pendientes — la OC entra al
      // inbox de Nivel1).
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.all,
      });
    },
  });
}
