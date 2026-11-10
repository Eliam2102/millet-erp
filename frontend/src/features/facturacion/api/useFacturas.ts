import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  facturacionKeys,
  type ListarFacturasFiltros,
} from '@/features/facturacion/api/keys';
import type {
  AplicarPedimentoCommand,
  AplicarPedimentoResponse,
  BandejaScoped,
  CancelacionBandejaItem,
  ComprobanteDetalleResponse,
  DatosSustitucionResponse,
  ConsultarCancelacionResponse,
  DescartarComprobanteResponse,
  EmisorDefaultsResponse,
  EmitirFacturaVentaCommand,
  EmitirFacturaVentaResponse,
  EnvioCorreoItem,
  EstadoSolicitudCancelacion,
  FacturaBandejaItem,
  IntentoTimbradoItem,
  NotaCreditoBonificacionCommand,
  NotaCreditoBonificacionResponse,
  ReenviarCorreoResponse,
  ReintentarTimbradoResponse,
  AdoptarCfdiTimbradoResponse,
  ComprobanteConEfectosPendientesItem,
  EfectoComprobanteItem,
  SolicitarCancelacionResponse,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/facturas';

/**
 * <c>useEmisorDefaults()</c> — RFC/razón social/régimen del emisor +
 * sucursal única activa (FAC-UX-PR3, cierra <EmisorDefaults>). Cambia
 * poco → staleTime largo; el form de emisión espera este query antes de
 * inicializar sus defaults.
 */
export function useEmisorDefaults() {
  return useQuery<EmisorDefaultsResponse>({
    queryKey: facturacionKeys.emisorDefaults(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<EmisorDefaultsResponse>(
        '/api/v1/facturacion/emisor-defaults',
        { signal },
      );
      return data;
    },
    staleTime: 30 * 60_000,
  });
}

/**
 * <c>useListarFacturas(filtros)</c> — bandeja de comprobantes emitidos.
 * CAJAS-PR6: expone el envelope {items, sinAsignarCount} completo (badge
 * "Sin asignar" [Decisión 12-B]); `alcance: 'sin-asignar'` restringe al
 * bucket (solo caja.leer-todas).
 */
export function useListarFacturas(filtros: ListarFacturasFiltros) {
  return useQuery<BandejaScoped<FacturaBandejaItem>>({
    queryKey: facturacionKeys.facturasList(filtros),
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      if (filtros.alcance != null) params.set('alcance', filtros.alcance);
      if (filtros.clase != null) params.set('clase', filtros.clase);
      if (filtros.ncPendiente) params.set('ncPendiente', 'true');
      if (filtros.q) params.set('q', filtros.q);
      const qs = params.toString();
      const { data } = await apiRequest<BandejaScoped<FacturaBandejaItem>>(
        qs ? `${BASE}?${qs}` : `${BASE}/`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useComprobante(id)</c> — detalle de una factura (líneas + timbre). */
/**
 * <c>useDatosSustitucion()</c> — FAC-08b: datos de la factura original para el
 * borrador de su sustituta. Falla con 422 si la original ya no se puede sustituir.
 */
export function useDatosSustitucion(id: string | null) {
  return useQuery<DatosSustitucionResponse>({
    queryKey: id != null ? [...facturacionKeys.facturaById(id), 'sustitucion'] : ['facturacion', 'noop'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useDatosSustitucion invocado sin id');
      const { data } = await apiRequest<DatosSustitucionResponse>(`${BASE}/${id}/sustitucion`, { signal });
      return data;
    },
    enabled: id != null,
    staleTime: 0,
    retry: false,
  });
}

export function useComprobante(id: string | null | undefined) {
  return useQuery<ComprobanteDetalleResponse>({
    queryKey: id != null ? facturacionKeys.facturaById(id) : ['facturacion', 'noop'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useComprobante invocado sin id');
      const { data } = await apiRequest<ComprobanteDetalleResponse>(
        `${BASE}/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/** <c>useComprobanteEnvios(id)</c> — bitácora de envío por correo (B6). */
export function useComprobanteEnvios(id: string | null | undefined) {
  return useQuery<EnvioCorreoItem[]>({
    queryKey:
      id != null ? facturacionKeys.facturaEnvios(id) : ['facturacion', 'noop-envios'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useComprobanteEnvios invocado sin id');
      const { data } = await apiRequest<EnvioCorreoItem[]>(
        `${BASE}/${id}/envios`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/**
 * <c>useReenviarCorreo()</c> — POST que encola el reenvío del CFDI por
 * correo (Idempotency-Key, ADR-0020). Invalida la bitácora de envíos.
 */
export function useReenviarCorreo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      destinatario: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ReenviarCorreoResponse>(
        `${BASE}/${args.id}/reenviar-correo`,
        {
          method: 'POST',
          body: { destinatario: args.destinatario },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.facturaEnvios(args.id),
      });
    },
  });
}

/**
 * <c>useAplicarPedimento()</c> — aplica el pedimento a una factura
 * retenida (PendientePedimento) y dispara su timbre (F7, Idempotency-Key).
 */
export function useAplicarPedimento() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      command: AplicarPedimentoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AplicarPedimentoResponse>(
        `${BASE}/${args.id}/pedimento`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturas() });
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.facturaById(args.id),
      });
    },
  });
}

/** <c>useCancelacionEstatus(id)</c> — estatus de cancelación SAT (B/F5). */
export function useCancelacionEstatus(id: string | null | undefined) {
  return useQuery<ConsultarCancelacionResponse>({
    queryKey:
      id != null
        ? facturacionKeys.facturaCancelacion(id)
        : ['facturacion', 'noop-cancelacion'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useCancelacionEstatus invocado sin id');
      const { data } = await apiRequest<ConsultarCancelacionResponse>(
        `/api/v1/facturacion/comprobantes/${id}/cancelar`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/**
 * <c>useSolicitarCancelacion()</c> — solicita la cancelación SAT 4.0
 * (Idempotency-Key). El motivo 01 exige UUID sustituto. F6.5: queda
 * pendiente de autorización del Contador General; aún no va al PAC.
 */
export function useSolicitarCancelacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      motivoSat: string;
      uuidSustituto: string | null;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<SolicitarCancelacionResponse>(
        `/api/v1/facturacion/comprobantes/${args.id}/cancelar`,
        {
          method: 'POST',
          body: { motivoSat: args.motivoSat, uuidSustituto: args.uuidSustituto },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturas() });
      // El comprobante cancelado puede ser una factura de anticipo (doc 13):
      // refresca su bandeja/detalle y el Control de Anticipos.
      queryClient.invalidateQueries({ queryKey: facturacionKeys.anticipos() });
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.facturaCancelacion(args.id),
      });
      queryClient.invalidateQueries({ queryKey: facturacionKeys.cancelaciones() });
    },
  });
}

/**
 * <c>useBandejaCancelaciones(estado)</c> — bandeja del Contador General (F6.5).
 * Sin estado (pestaña «Todos», B3) trae todas las solicitudes.
 */
export function useBandejaCancelaciones(estado: EstadoSolicitudCancelacion | undefined) {
  return useQuery<CancelacionBandejaItem[]>({
    queryKey: facturacionKeys.cancelacionesBandeja(estado ?? 'todos'),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CancelacionBandejaItem[]>(
        `/api/v1/facturacion/comprobantes/cancelaciones?${estado ? `estado=${estado}` : 'todos=true'}`,
        { signal },
      );
      return data;
    },
    staleTime: 0,
  });
}

/**
 * <c>useDecidirCancelacion()</c> — F6.5: el Contador General autoriza (se envía
 * al PAC) o rechaza con motivo; Ingresos puede retirar una pendiente.
 * Idempotency-Key en las tres.
 */
export function useDecidirCancelacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      accion: 'autorizar' | 'rechazar' | 'retirar';
      motivo?: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<SolicitarCancelacionResponse>(
        `/api/v1/facturacion/comprobantes/${args.id}/cancelar/${args.accion}`,
        {
          method: 'POST',
          body: args.accion === 'rechazar' ? { motivo: args.motivo ?? '' } : undefined,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturas() });
      queryClient.invalidateQueries({ queryKey: facturacionKeys.anticipos() });
      queryClient.invalidateQueries({ queryKey: facturacionKeys.cancelaciones() });
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.facturaCancelacion(args.id),
      });
    },
  });
}

/**
 * <c>useReintentarTimbrado()</c> — reintenta el timbrado de un comprobante
 * en TimbradoFallido (cualquier tipo — mismo folio interno, no re-emite;
 * Idempotency-Key). <c>confirmarNoDuplicado</c> es obligatorio cuando el
 * fallo fue por código ambiguo (PAC_TIMEOUT y variantes): el operador
 * verifica primero en FiscalAPI que no exista timbre. Invalida la raíz de
 * facturación: el comprobante puede ser factura, NC, REPP o carta porte.
 */
export function useReintentarTimbrado() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      confirmarNoDuplicado: boolean;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ReintentarTimbradoResponse>(
        `/api/v1/facturacion/comprobantes/${args.id}/reintentar-timbrado`,
        {
          method: 'POST',
          body: { confirmarNoDuplicado: args.confirmarNoDuplicado },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.all });
    },
  });
}

/**
 * <c>useAdoptarCfdi()</c> — M-284: adopta el CFDI que el PAC sí timbró (XML descargado del
 * portal del PAC) en un comprobante cuyo timbrado no se confirmó. Con <c>soloRevisar</c> solo
 * coteja ERP contra XML; sin él, adopta y aplica los efectos del timbrado una sola vez.
 */
export function useAdoptarCfdi() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      xml: string;
      soloRevisar: boolean;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AdoptarCfdiTimbradoResponse>(
        `/api/v1/facturacion/comprobantes/${args.id}/adoptar-cfdi`,
        {
          method: 'POST',
          body: { xml: args.xml, soloRevisar: args.soloRevisar },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (data) => {
      if (data.adoptado) queryClient.invalidateQueries({ queryKey: facturacionKeys.all });
    },
  });
}

/**
 * <c>useDescartarComprobante()</c> — descarta un comprobante en
 * TimbradoFallido que NO se reintentará ([Decisión 01-G] G3): terminal,
 * quema el folio a conciencia y libera el pedido si esta factura lo tenía
 * tomado. Invalida la raíz (el pedido liberado vuelve a la bandeja).
 */
export function useDescartarComprobante() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<DescartarComprobanteResponse>(
        `/api/v1/facturacion/comprobantes/${args.id}/descartar`,
        { method: 'POST', idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.all });
    },
  });
}

/** M-285: efectos posteriores al timbrado del comprobante y su estado. */
export function useEfectosComprobante(id: string | null | undefined) {
  return useQuery<EfectoComprobanteItem[]>({
    queryKey: id != null ? facturacionKeys.comprobanteEfectos(id) : ['facturacion', 'noop-efectos'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useEfectosComprobante invocado sin id');
      const { data } = await apiRequest<EfectoComprobanteItem[]>(
        `/api/v1/facturacion/comprobantes/${id}/efectos`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/** M-285: comprobantes timbrados con efectos pendientes (bandeja de contingencias). */
export function useComprobantesConEfectosPendientes(enabled = true) {
  return useQuery<ComprobanteConEfectosPendientesItem[]>({
    queryKey: facturacionKeys.efectosPendientes(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ComprobanteConEfectosPendientesItem[]>(
        '/api/v1/facturacion/comprobantes/efectos-pendientes',
        { signal },
      );
      return data;
    },
    enabled,
    staleTime: 0,
  });
}

/** M-285: aplica solo los efectos pendientes; no vuelve a timbrar. */
export function useReintentarEfectos() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<EfectoComprobanteItem[]>(
        `/api/v1/facturacion/comprobantes/${args.id}/reintentar-efectos`,
        { method: 'POST', idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.all });
    },
  });
}

/**
 * <c>useIntentosTimbrado(id)</c> — bitácora de intentos de timbrado del
 * comprobante ([Decisión 01-G] G4): una fila por llamada al PAC, la más
 * reciente primero.
 */
export function useIntentosTimbrado(id: string | null | undefined) {
  return useQuery<IntentoTimbradoItem[]>({
    queryKey:
      id != null
        ? facturacionKeys.comprobanteIntentos(id)
        : ['facturacion', 'noop-intentos'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useIntentosTimbrado invocado sin id');
      const { data } = await apiRequest<IntentoTimbradoItem[]>(
        `/api/v1/facturacion/comprobantes/${id}/intentos-timbrado`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/**
 * <c>useEmitirNcBonificacion()</c> — emite una NC por bonificación
 * (relación 01) sobre una factura (Idempotency-Key).
 */
export function useEmitirNcBonificacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: NotaCreditoBonificacionCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<NotaCreditoBonificacionResponse>(
        '/api/v1/facturacion/notas-credito/bonificacion',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturas() });
      queryClient.invalidateQueries({
        queryKey: facturacionKeys.facturaById(args.command.facturaVentaId),
      });
    },
  });
}

/**
 * <c>useSolicitarReembolsoNc()</c> — pide a Tesorería el reembolso en dinero de una NC
 * timbrada (T9.6 · CA6.17; Idempotency-Key). No cambia el saldo de la factura, pero
 * refresca el detalle para marcar la NC como «Solicitado».
 */
export function useSolicitarReembolsoNc() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      notaCreditoId: string;
      importe: number;
      motivo: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<{
        notaCreditoId: string;
        importe: number;
        reembolsoSolicitadoEn: string;
      }>(`/api/v1/facturacion/notas-credito/${args.notaCreditoId}/solicitar-reembolso`, {
        method: 'POST',
        body: { importe: args.importe, motivo: args.motivo },
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturas() });
    },
  });
}

/**
 * <c>useEmitirFactura()</c> — POST de emisión (sella + timbra). Mutación
 * con impacto fiscal → Idempotency-Key obligatorio (ADR-0020). El stub
 * de timbrado devuelve <c>Timbrado</c> síncrono con UUID falso en dev.
 */
export function useEmitirFactura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: EmitirFacturaVentaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EmitirFacturaVentaResponse>(`${BASE}/`, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturas() });
    },
  });
}
