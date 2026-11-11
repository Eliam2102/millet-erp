import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  ComparacionProformaResponse,
  EmitirFacturaVentaCommand,
  ProformaConformidadResponse,
  ProformaPedidoResponse,
  RegistrarConformidadProformaBody,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/pedidos-facturables';

/**
 * <c>useUltimaProforma(pedidoId)</c> — última proforma generada del pedido
 * (U1.8a). Un 204 significa «el pedido aún no tiene proforma» y se expone
 * como <c>null</c>, no como error.
 */
export function useUltimaProforma(pedidoId: string | null | undefined, enabled = true) {
  return useQuery<ProformaPedidoResponse | null>({
    queryKey:
      pedidoId != null
        ? facturacionKeys.pedidoUltimaProforma(pedidoId)
        : ['facturacion', 'noop'],
    queryFn: async ({ signal }) => {
      if (pedidoId == null) throw new Error('useUltimaProforma invocado sin id');
      const { data } = await apiRequest<ProformaPedidoResponse | undefined>(
        `${BASE}/${pedidoId}/proformas/ultima`,
        { signal },
      );
      return data ?? null;
    },
    enabled: pedidoId != null && enabled,
  });
}

/**
 * <c>useGenerarProforma()</c> — POST que arma y guarda una nueva versión de
 * la proforma del pedido. No reserva folio fiscal; lleva Idempotency-Key
 * como los demás POST del módulo (ADR-0020).
 */
export function useGenerarProforma() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { pedidoId: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<ProformaPedidoResponse>(
        `${BASE}/${args.pedidoId}/proformas`,
        { method: 'POST', idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: (data, args) => {
      queryClient.setQueryData(facturacionKeys.pedidoUltimaProforma(args.pedidoId), data);
    },
  });
}

/**
 * <c>useRegistrarConformidadProforma()</c> — M-234: registra la conformidad del cliente con UNA versión
 * de la proforma (quién, cuándo, medio). Una proforma vencida responde 422 FAC_PROFORMA_VENCIDA.
 */
export function useRegistrarConformidadProforma() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      pedidoId: string;
      proformaId: string;
      body: RegistrarConformidadProformaBody;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ProformaConformidadResponse>(
        `${BASE}/${args.pedidoId}/proformas/${args.proformaId}/conformidad`,
        { method: 'POST', body: args.body, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      void queryClient.invalidateQueries({
        queryKey: facturacionKeys.pedidoUltimaProforma(args.pedidoId),
      });
    },
  });
}

/**
 * <c>useFacturarDesdeProforma()</c> — M-340: emite la factura desde la versión aprobada de la proforma, sin recaptura.
 * Un pedido cambiado después de aprobar responde 422 FAC_PROFORMA_PEDIDO_CAMBIO y deja la conformidad invalidada: por eso
 * se refresca la proforma también al fallar.
 */
export function useFacturarDesdeProforma() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { pedidoId: string; proformaId: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<{ id: string; folio: string; estado: string; uuid: string | null; total: number }>(
        `${BASE}/${args.pedidoId}/proformas/${args.proformaId}/facturar`,
        { method: 'POST', idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSettled: (_d, _e, args) => {
      void queryClient.invalidateQueries({ queryKey: facturacionKeys.pedidoUltimaProforma(args.pedidoId) });
      void queryClient.invalidateQueries({ queryKey: facturacionKeys.pedidos() });
    },
  });
}

/**
 * <c>compararProforma(pedidoId, command)</c> — compara lo que se va a timbrar
 * (mismo payload de emisión) contra la última proforma del pedido. Lectura
 * pura: sin Idempotency-Key.
 */
export async function compararProforma(
  pedidoId: string,
  command: EmitirFacturaVentaCommand,
): Promise<ComparacionProformaResponse> {
  const { data } = await apiRequest<ComparacionProformaResponse>(
    `${BASE}/${pedidoId}/proformas/comparar`,
    { method: 'POST', body: command },
  );
  return data;
}
