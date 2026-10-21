import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  BandejaScoped,
  EmitirReppCommand,
  EmitirReppResponse,
  FacturaCobrablePpdItem,
  ReppBandejaItem,
  ReppDetalleResponse,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/repp';

/** <c>useListarRepp(estado, alcance?)</c> — bandeja de complementos de pago (B9). */
export function useListarRepp(estado: number | undefined, alcance?: 'sin-asignar') {
  return useQuery<BandejaScoped<ReppBandejaItem>>({
    queryKey: facturacionKeys.reppList({ estado: estado ?? null, alcance: alcance ?? null }),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (estado != null) params.set('estado', String(estado));
      if (alcance != null) params.set('alcance', alcance);
      const qs = params.toString();
      // CAJAS-PR6: expone el envelope {items, sinAsignarCount} completo
      // (badge "Sin asignar" [Decisión 12-B], solo caja.leer-todas).
      const { data } = await apiRequest<BandejaScoped<ReppBandejaItem>>(
        qs ? `${BASE}?${qs}` : `${BASE}/`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useRepp(id)</c> — detalle de un REPP con facturas cubiertas (B9). */
export function useRepp(id: string | null | undefined) {
  return useQuery<ReppDetalleResponse>({
    queryKey: id != null ? facturacionKeys.reppById(id) : ['facturacion', 'noop-repp'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useRepp invocado sin id');
      const { data } = await apiRequest<ReppDetalleResponse>(`${BASE}/${id}`, {
        signal,
      });
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/**
 * <c>useFacturasCobrablesPpd(receptorRfc?)</c> — facturas PPD timbradas con
 * saldo por cobrar &gt; 0, candidatas de un REPP (alimenta el
 * <c>FacturaPpdPicker</c>; cierra PLATFORM-TODO(&lt;FacturaPicker&gt;)).
 * <c>receptorRfc</c> es el RFC del cliente elegido en la cabecera (regla
 * <c>REPP_MULTIPLES_CLIENTES</c>). Sin RFC no consulta (tarea 86e3tfd8k:
 * primero se elige el cliente); el filtrado fino es client-side en el
 * combobox.
 */
export function useFacturasCobrablesPpd(receptorRfc?: string | null) {
  const rfc = (receptorRfc ?? '').trim().toUpperCase();
  return useQuery<FacturaCobrablePpdItem[]>({
    queryKey: facturacionKeys.reppFacturasCobrables(rfc === '' ? null : rfc),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ receptorRfc: rfc });
      const { data } = await apiRequest<FacturaCobrablePpdItem[]>(
        `${BASE}/facturas-cobrables?${params.toString()}`,
        { signal },
      );
      return data;
    },
    enabled: rfc !== '',
  });
}

/**
 * <c>useEmitirRepp()</c> — emite un complemento de pago (Pago 2.0,
 * multi-factura) con Idempotency-Key. El stub timbra síncrono.
 */
export function useEmitirRepp() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: EmitirReppCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EmitirReppResponse>(`${BASE}/`, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.repp() });
      // Los pagos afectan saldos de facturas → refresca su familia.
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturas() });
    },
  });
}
