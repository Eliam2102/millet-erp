import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  AutorizacionAperturaResponse,
  CajaAjusteItem,
  CajaMovimientoRegistradoResponse,
  CajaSesionMutadaResponse,
  CobroFormaPagoInput,
  CobroMostradorItem,
  CobroMostradorResponse,
  ComprobanteCobrableItem,
  LiquidacionRutaCobroInput,
  LiquidacionRutaResponse,
  SesionActualResponse,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/cajas';
const COBROS = '/api/v1/facturacion/cobros';

/**
 * <c>useSesionActual()</c> — sesión vigente del cajero (panel "Mi caja",
 * CAJAS-PR6). `sesion` null = sin sesión; `diaAnteriorPendiente` = solo
 * arqueo y cierre extemporáneo (12-cajas.md §5.2). El If-Match de las
 * mutaciones sale de `sesion.version` (respuesta, no header ETag).
 */
export function useSesionActual() {
  return useQuery<SesionActualResponse>({
    queryKey: facturacionKeys.sesionActual(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<SesionActualResponse>(`${BASE}/sesion-actual`, {
        signal,
      });
      return data;
    },
    staleTime: 0,
  });
}

function useInvalidarSesion() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: facturacionKeys.sesiones() });
    void queryClient.invalidateQueries({ queryKey: facturacionKeys.cajas() });
  };
}

/** <c>useAbrirSesion()</c> — POST apertura (Idempotency-Key; autorización si la caja es ajena). */
export function useAbrirSesion() {
  const invalidar = useInvalidarSesion();
  return useMutation({
    mutationFn: async (args: {
      cajaId: string;
      body: {
        sucursalId: string;
        fondoApertura: number;
        autorizacionAperturaId?: string | null;
      };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CajaSesionMutadaResponse>(
        `${BASE}/${args.cajaId}/sesiones`,
        { method: 'POST', body: args.body, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: invalidar,
  });
}

/** <c>useRegistrarMovimiento()</c> — depósito/retiro manual (Idempotency-Key). */
export function useRegistrarMovimiento() {
  const invalidar = useInvalidarSesion();
  return useMutation({
    mutationFn: async (args: {
      sesionId: string;
      body: {
        tipo: number;
        formaPago: string;
        importe: number;
        descripcion: string;
        referencia?: string | null;
      };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CajaMovimientoRegistradoResponse>(
        `${BASE}/sesiones/${args.sesionId}/movimientos`,
        { method: 'POST', body: args.body, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: invalidar,
  });
}

function useTransicionSesion(accion: 'arqueo' | 'cierre' | 'reabrir') {
  const invalidar = useInvalidarSesion();
  return useMutation({
    mutationFn: async (args: {
      sesionId: string;
      version: number;
      body?: { efectivoDeclarado: number; notasCierre?: string | null };
    }) => {
      const { data } = await apiRequest<CajaSesionMutadaResponse>(
        `${BASE}/sesiones/${args.sesionId}/${accion}`,
        {
          method: 'POST',
          body: args.body ?? {},
          // apiRequest envuelve el valor en comillas al armar el header.
          ifMatch: String(args.version),
        },
      );
      return data;
    },
    onSuccess: invalidar,
  });
}

/** Transición Abierta → EnArqueo (calcula el esperado por forma; If-Match). */
export function useIniciarArqueo() {
  return useTransicionSesion('arqueo');
}

/** Cierre con contado físico (permiso caja.liquidar; If-Match). */
export function useCerrarSesion() {
  return useTransicionSesion('cierre');
}

/** EnArqueo → Abierta (permiso caja.supervisar; If-Match). */
export function useReabrirSesion() {
  return useTransicionSesion('reabrir');
}

/** <c>useCrearAutorizacionApertura()</c> — supervisor autoriza apertura de caja ajena ([12-1]). */
export function useCrearAutorizacionApertura() {
  return useMutation({
    mutationFn: async (args: {
      cajaId: string;
      body: { cajeroUsuarioId: string; motivo: string };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AutorizacionAperturaResponse>(
        `${BASE}/${args.cajaId}/autorizaciones-apertura`,
        { method: 'POST', body: args.body, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
  });
}

// ─── Cobros de mostrador (CAJAS-PR4 backend) ─────────────────────────

/** <c>useListarCobros(sesionId)</c> — cobros registrados en una sesión. */
export function useListarCobros(sesionId: string | null | undefined) {
  return useQuery<CobroMostradorItem[]>({
    queryKey:
      sesionId != null ? facturacionKeys.cobros(sesionId) : ['facturacion', 'noop-cobros'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CobroMostradorItem[]>(
        `${COBROS}?sesionId=${sesionId}`,
        { signal },
      );
      return data;
    },
    enabled: sesionId != null,
    staleTime: 0,
  });
}

/**
 * <c>useRegistrarCobro()</c> — cobra un comprobante timbrado a la sesión
 * abierta del cajero ([Decisión 12-E]: emitir y cobrar son dos comandos).
 * `origen` 1 = Mostrador, 2 = LiquidacionRuta ([12-7]).
 */
export function useRegistrarCobro() {
  const invalidar = useInvalidarSesion();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      body: {
        comprobanteId: string;
        formasPago: CobroFormaPagoInput[];
        origen?: number;
      };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CobroMostradorResponse>(`${COBROS}/`, {
        method: 'POST',
        body: args.body,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: (cobro) => {
      invalidar();
      void queryClient.invalidateQueries({
        queryKey: facturacionKeys.cobros(cobro.cajaSesionId),
      });
      void queryClient.invalidateQueries({
        queryKey: facturacionKeys.facturaById(cobro.comprobanteId),
      });
    },
  });
}

/** <c>useCancelarCobro()</c> — supervisor cancela; reversa o ajuste pendiente ([12-C]). */
export function useCancelarCobro() {
  const invalidar = useInvalidarSesion();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      cobroId: string;
      motivo: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CobroMostradorResponse>(
        `${COBROS}/${args.cobroId}/cancelar`,
        {
          method: 'POST',
          body: { motivo: args.motivo },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (cobro) => {
      invalidar();
      void queryClient.invalidateQueries({
        queryKey: facturacionKeys.cobros(cobro.cajaSesionId),
      });
    },
  });
}

// ─── Liquidación de ruta + ajustes de caja (CAJAS-PR7) ───────────────

/**
 * <c>useComprobantesCobrables(search)</c> — comprobantes timbrados sin cobro
 * vigente en el alcance del cajero: candidatos de la liquidación de ruta
 * ([12-7]) y del cobro unitario.
 */
export function useComprobantesCobrables(search: string | null, enabled = true) {
  return useQuery<ComprobanteCobrableItem[]>({
    queryKey: facturacionKeys.cobrables(search),
    queryFn: async ({ signal }) => {
      const params = search != null && search !== '' ? `?search=${encodeURIComponent(search)}` : '';
      const { data } = await apiRequest<ComprobanteCobrableItem[]>(
        `${COBROS}/cobrables${params}`,
        { signal },
      );
      return data;
    },
    enabled,
    staleTime: 0,
  });
}

/**
 * <c>useLiquidarRuta()</c> — batch todo-o-nada de cobros con
 * <c>origen = LiquidacionRuta</c> a la sesión abierta del cajero ([12-7]).
 */
export function useLiquidarRuta() {
  const invalidar = useInvalidarSesion();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      cobros: LiquidacionRutaCobroInput[];
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<LiquidacionRutaResponse>(
        `${COBROS}/liquidacion-ruta`,
        { method: 'POST', body: { cobros: args.cobros }, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: (liquidacion) => {
      invalidar();
      void queryClient.invalidateQueries({
        queryKey: facturacionKeys.cobros(liquidacion.cajaSesionId),
      });
    },
  });
}

/**
 * <c>useAjustesCaja(cajaId)</c> — ajustes pendientes de drenar en la próxima
 * apertura (y aplicados con <c>incluirAplicados</c>) ([Decisión 12-C]).
 */
export function useAjustesCaja(cajaId: string | null, incluirAplicados = false) {
  return useQuery<CajaAjusteItem[]>({
    queryKey:
      cajaId != null
        ? facturacionKeys.ajustesCaja(cajaId, incluirAplicados)
        : ['facturacion', 'noop-ajustes'],
    queryFn: async ({ signal }) => {
      const params = incluirAplicados ? '?incluirAplicados=true' : '';
      const { data } = await apiRequest<CajaAjusteItem[]>(
        `${BASE}/${cajaId}/ajustes${params}`,
        { signal },
      );
      return data;
    },
    enabled: cajaId != null,
    staleTime: 0,
  });
}
