import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * 86e3tacbn: conformidad del servicio por OC. `confirmar` → POST /confirmar-servicio (comentario opcional);
 * `deshacer` → POST /deshacer-confirmacion-servicio y `no-conforme` → POST /servicio-no-conforme (motivo obligatorio).
 */
export type AccionServicioArgs = { ordenCompraId: string; idempotencyKey: string } & (
  | { accion: 'confirmar'; texto: string | null }
  | { accion: 'deshacer' | 'no-conforme'; texto: string }
);

const RUTA = {
  confirmar: 'confirmar-servicio',
  deshacer: 'deshacer-confirmacion-servicio',
  'no-conforme': 'servicio-no-conforme',
} as const;

export function useAccionServicioOc() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, AccionServicioArgs>({
    mutationFn: async (args) => {
      await apiRequest<void>(`/api/v1/compras/ordenes/${args.ordenCompraId}/${RUTA[args.accion]}`, {
        method: 'POST',
        body: args.accion === 'confirmar' ? { comentario: args.texto } : { motivo: args.texto },
        idempotencyKey: args.idempotencyKey,
      });
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({ queryKey: ordenesKeys.detail(ordenCompraId) });
      queryClient.invalidateQueries({ queryKey: ordenesKeys.all });
    },
  });
}
