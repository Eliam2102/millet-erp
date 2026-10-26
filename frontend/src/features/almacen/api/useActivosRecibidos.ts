import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/** Fila de <c>GET /almacen/recepciones/ordenes/{oc}/activos</c>. */
export interface ActivoRecibido {
  id: string;
  recepcionId: string;
  /** Null en filas históricas. */
  folio: string | null;
  lineaOcId: string;
  cantidad: number;
  recibidaEn: string;
  anulada: boolean;
  anuladaEn: string | null;
  motivoAnulacion: string | null;
}

export interface AnularRecepcionActivoResponse {
  recepcionId: string;
  folio: string | null;
  lineasAnuladas: number;
  yaAnulada: boolean;
}

// Cuelga de almacenKeys.recepciones(): las invalidaciones de recepciones lo refrescan.
const activosKey = (ordenCompraId: string) =>
  [...almacenKeys.recepciones(), 'activos', ordenCompraId] as const;

/** Recepciones de activo fijo de la OC (no generan movimiento de inventario). */
export function useActivosRecibidos(ordenCompraId: string | undefined) {
  return useQuery({
    queryKey: activosKey(ordenCompraId ?? ''),
    queryFn: async ({ signal }) =>
      (
        await apiRequest<ActivoRecibido[]>(
          `/api/v1/almacen/recepciones/ordenes/${ordenCompraId}/activos`,
          { signal },
        )
      ).data,
    enabled: !!ordenCompraId,
  });
}

export function useAnularRecepcionActivo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      ordenCompraId: string;
      recepcionId: string;
      motivo: string;
      idempotencyKey: string;
    }) =>
      (
        await apiRequest<AnularRecepcionActivoResponse>(
          `/api/v1/almacen/recepciones/ordenes/${args.ordenCompraId}/activos/${args.recepcionId}/anular`,
          { method: 'POST', body: { motivo: args.motivo }, idempotencyKey: args.idempotencyKey },
        )
      ).data,
    onSuccess: (_d, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.recepciones() });
      queryClient.invalidateQueries({ queryKey: ['almacen', 'recepcion-recibibles'] });
      queryClient.invalidateQueries({ queryKey: ordenesKeys.detail(args.ordenCompraId) });
    },
  });
}
