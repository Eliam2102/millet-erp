import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  AutorizacionActivoItem,
  AutorizarVentaActivoCommand,
  AutorizarVentaActivoResponse,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/activos';

/** <c>useAutorizacionesActivo(estado)</c> — bandeja de autorizaciones (B13). */
export function useAutorizacionesActivo(estado: number | undefined) {
  return useQuery<AutorizacionActivoItem[]>({
    queryKey: facturacionKeys.activosAutorizaciones(
      estado != null ? String(estado) : null,
    ),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (estado != null) params.set('estado', String(estado));
      const qs = params.toString();
      const { data } = await apiRequest<AutorizacionActivoItem[]>(
        qs ? `${BASE}/autorizaciones?${qs}` : `${BASE}/autorizaciones`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useAutorizarVentaActivo()</c> — el Contador General autoriza la venta
 * de un activo (calcula valor neto en libros + utilidad/pérdida). El
 * <c>autorizacionId</c> resultante es obligatorio al emitir la factura de
 * venta de activo fijo. Idempotency-Key (ADR-0020).
 */
export function useAutorizarVentaActivo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: AutorizarVentaActivoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AutorizarVentaActivoResponse>(
        `${BASE}/autorizar`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: [...facturacionKeys.all, 'activos'],
      });
    },
  });
}
