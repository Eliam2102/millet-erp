import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { AutorizarOcValues } from '@/features/compras/ordenes/schemas/autorizar';

export interface AutorizarOrdenCompraMutationArgs {
  ordenCompraId: string;
  command: AutorizarOcValues;
  /** UUID v4 estable por intento — ADR-0020. */
  idempotencyKey: string;
}

/**
 * <c>useAutorizarOrdenCompra()</c> — POST <c>/{id}/autorizaciones</c>
 * (UF4-PR1). Registra firma N1 o N2. Backend valida permiso según
 * <c>nivel</c>:
 * <list>
 *   <item><c>Nivel1</c> requiere <c>compras.ordenes.autorizar-nivel1</c>;
 *   transiciona <c>EnAutorizacionJefeCompras</c> →
 *   <c>EnAutorizacionDireccion</c>.</item>
 *   <item><c>Nivel2</c> requiere <c>compras.ordenes.autorizar-nivel2</c>;
 *   transiciona <c>EnAutorizacionDireccion</c> → <c>Autorizada</c>,
 *   setea <c>FechaContabilizacion</c> + emite
 *   <c>OrdenCompraAutorizadaEvent</c> (consumido por PDF +
 *   Notificaciones).</item>
 * </list>
 */
export function useAutorizarOrdenCompra() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, AutorizarOrdenCompraMutationArgs>({
    mutationFn: async ({ ordenCompraId, command, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/ordenes/${ordenCompraId}/autorizaciones`,
        {
          method: 'POST',
          body: command,
          idempotencyKey,
        },
      );
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(ordenCompraId),
      });
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.all,
      });
    },
  });
}
