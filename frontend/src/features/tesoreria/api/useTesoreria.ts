import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  tesoreriaKeys,
  type DepositosFiltros,
  type MovimientosFiltros,
  type PasivosFiltros,
} from './keys';
import type {
  AplicacionPagoItem,
  BeneficiarioTipo,
  CuentaSaldoResponse,
  DepositoConfirmacionResponse,
  MovimientoBancarioResponse,
  MovimientoDetalleResponse,
  PagedResponse,
  PagoACuentaAbiertoResponse,
  PagoACuentaResponse,
  PagoProveedorResponse,
  PasivoPendienteResponse,
  ReporteTesoreriaBackend,
  ReppPendienteResponse,
  ReppRecibidoResponse,
} from './types';

const BASE = '/api/v1/tesoreria';

// ─── Cuentas ─────────────────────────────────────────────────────────

/** <c>useCuentasBancarias()</c> — catálogo con saldo (GET only en v1 [TES-7]). */
export function useCuentasBancarias(soloActivas = true) {
  return useQuery<CuentaSaldoResponse[]>({
    queryKey: tesoreriaKeys.cuentas(soloActivas),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CuentaSaldoResponse[]>(
        `${BASE}/cuentas?soloActivas=${soloActivas}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useCrearCuentaBancaria()</c> — alta del catálogo (TES-7 revisada,
 * permiso <c>tesoreria.cuentas.administrar</c>). 409 si (empresa, número)
 * ya existe.
 */
export function useCrearCuentaBancaria() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: {
        banco: string;
        numeroCuenta: string;
        clabe?: string;
        moneda: string;
        cuentaContableRef?: string;
        perfilExtracto?: string;
      };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CuentaSaldoResponse>(`${BASE}/cuentas`, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.cuentasAll() });
    },
  });
}

/**
 * <c>useActualizarCuentaBancaria()</c> — edición; el número de cuenta es
 * inmutable y la CLABE es write-only (undefined = sin cambio,
 * <c>limpiarClabe</c> = borrar) porque el catálogo llega enmascarado.
 */
export function useActualizarCuentaBancaria() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      cuentaId: string;
      body: {
        banco: string;
        moneda: string;
        clabe?: string;
        limpiarClabe: boolean;
        cuentaContableRef?: string;
        perfilExtracto?: string;
      };
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CuentaSaldoResponse>(
        `${BASE}/cuentas/${args.cuentaId}`,
        {
          method: 'PUT',
          body: args.body,
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.cuentasAll() });
    },
  });
}

/** <c>useCambiarEstadoCuentaBancaria()</c> — activar/desactivar del catálogo. */
export function useCambiarEstadoCuentaBancaria() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      cuentaId: string;
      activa: boolean;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CuentaSaldoResponse>(
        `${BASE}/cuentas/${args.cuentaId}/${args.activa ? 'activar' : 'desactivar'}`,
        {
          method: 'POST',
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.cuentasAll() });
    },
  });
}

// ─── Movimientos ─────────────────────────────────────────────────────

/** <c>useMovimientos(filtros)</c> — libro paginado server-side (P1). */
export function useMovimientos(filtros: MovimientosFiltros = {}) {
  return useQuery<PagedResponse<MovimientoBancarioResponse>>({
    queryKey: tesoreriaKeys.movimientosList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.cuentaBancariaId)
        params.set('cuentaBancariaId', filtros.cuentaBancariaId);
      if (filtros.sentido != null) params.set('sentido', String(filtros.sentido));
      if (filtros.estadoAplicacion != null)
        params.set('estadoAplicacion', String(filtros.estadoAplicacion));
      if (filtros.estadoConciliacion != null)
        params.set('estadoConciliacion', String(filtros.estadoConciliacion));
      if (filtros.desde) params.set('desde', filtros.desde);
      if (filtros.hasta) params.set('hasta', filtros.hasta);
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<MovimientoBancarioResponse>>(
        qs ? `${BASE}/movimientos?${qs}` : `${BASE}/movimientos`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useMovimiento(id)</c> — detalle con aplicaciones y contramovimientos (P3). */
export function useMovimiento(id: string | null) {
  return useQuery<MovimientoDetalleResponse>({
    queryKey: tesoreriaKeys.movimientoById(id ?? ''),
    enabled: id != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<MovimientoDetalleResponse>(
        `${BASE}/movimientos/${id}`,
        { signal },
      );
      return data;
    },
  });
}

// ─── Pasivos (bandeja de pagos) ──────────────────────────────────────

/** <c>usePasivosPendientes(filtros)</c> — bandeja P2 server-side. */
export function usePasivosPendientes(filtros: PasivosFiltros = {}) {
  return useQuery<PagedResponse<PasivoPendienteResponse>>({
    queryKey: tesoreriaKeys.pasivosList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.proveedorId) params.set('proveedorId', filtros.proveedorId);
      if (filtros.moneda) params.set('moneda', filtros.moneda);
      if (filtros.venceDesde) params.set('venceDesde', filtros.venceDesde);
      if (filtros.venceHasta) params.set('venceHasta', filtros.venceHasta);
      if (filtros.montoMinimo != null)
        params.set('montoMinimo', String(filtros.montoMinimo));
      if (filtros.montoMaximo != null)
        params.set('montoMaximo', String(filtros.montoMaximo));
      if (filtros.soloConSaldo != null)
        params.set('soloConSaldo', String(filtros.soloConSaldo));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<PasivoPendienteResponse>>(
        qs ? `${BASE}/pasivos-pendientes?${qs}` : `${BASE}/pasivos-pendientes`,
        { signal },
      );
      return data;
    },
  });
}

// ─── Pagos a cuenta (TES-FE-PR4) ─────────────────────────────────────

/** <c>usePagosACuentaAbiertos(filtros)</c> — read model con antigüedad (§3.4). */
export function usePagosACuentaAbiertos(
  filtros: {
    proveedorId?: string;
    incluirParciales?: boolean;
    offset?: number;
    limit?: number;
  } = {},
) {
  return useQuery<PagedResponse<PagoACuentaAbiertoResponse>>({
    queryKey: tesoreriaKeys.pagosCuentaList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.proveedorId) params.set('proveedorId', filtros.proveedorId);
      if (filtros.incluirParciales != null)
        params.set('incluirParciales', String(filtros.incluirParciales));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<PagoACuentaAbiertoResponse>>(
        qs ? `${BASE}/pagos-cuenta?${qs}` : `${BASE}/pagos-cuenta`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useRegistrarPagoACuenta()</c> — POST con gate RN-2 (422 si hay abierto del proveedor). */
export function useRegistrarPagoACuenta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: {
        cuentaBancariaId: string;
        monto: number;
        fechaValor: string;
        motivo: string;
        proveedorId?: string;
        referenciaBancaria?: string;
        conceptoId?: string;
      };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PagoACuentaResponse>(
        `${BASE}/pagos-cuenta`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all });
    },
  });
}

/** <c>useLigarPagoACuenta()</c> — liga tardía sin re-desembolso (emite aplicado.v1). */
export function useLigarPagoACuenta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      movimientoId: string;
      facturaProveedorId: string;
      importe: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PagoProveedorResponse>(
        `${BASE}/pagos-cuenta/${args.movimientoId}/ligar`,
        {
          method: 'POST',
          body: {
            facturaProveedorId: args.facturaProveedorId,
            importe: args.importe,
          },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all });
    },
  });
}

// ─── Depósitos por confirmar (TES-FE-PR4b, §3.3 / TES-9) ────────────

/** <c>useDepositos(filtros)</c> — bandeja P2 de propuestas CxC + expectativas de Caja. */
export function useDepositos(filtros: DepositosFiltros = {}) {
  return useQuery<PagedResponse<DepositoConfirmacionResponse>>({
    queryKey: tesoreriaKeys.depositosList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.soloPropuestas != null)
        params.set('soloPropuestas', String(filtros.soloPropuestas));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<DepositoConfirmacionResponse>>(
        qs ? `${BASE}/depositos?${qs}` : `${BASE}/depositos`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useConfirmarDeposito()</c> — RN-6: liga un movimiento de INGRESO
 * identificado; en propuestas de CxC publica pago-cliente.confirmado.v1
 * (Facturación emite el REPP; confirmar ≠ timbrado). Concurrencia por
 * X-Expected-Version (BaseEntity.Version, patrón CxC).
 */
export function useConfirmarDeposito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      depositoId: string;
      movimientoBancarioId: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<DepositoConfirmacionResponse>(
        `${BASE}/depositos/${args.depositoId}/confirmar`,
        {
          method: 'POST',
          body: { movimientoBancarioId: args.movimientoBancarioId },
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all });
    },
  });
}

/** <c>useRechazarDeposito()</c> — rechazo con motivo [T-G7]; CxC re-propone. */
export function useRechazarDeposito() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      depositoId: string;
      motivo: string;
      versionEsperada: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<DepositoConfirmacionResponse>(
        `${BASE}/depositos/${args.depositoId}/rechazar`,
        {
          method: 'POST',
          body: { motivo: args.motivo },
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all });
    },
  });
}

/** <c>useRegistrarMovimientoIngreso()</c> — alta manual de depósito (POST /movimientos, TES-PR2). */
export function useRegistrarMovimientoIngreso() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: {
        cuentaBancariaId: string;
        monto: number;
        fechaValor: string;
        referenciaBancaria?: string;
        conceptoId?: string;
        beneficiarioTipo?: BeneficiarioTipo;
        beneficiarioRef?: string;
      };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<MovimientoBancarioResponse>(
        `${BASE}/movimientos`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.movimientos() });
    },
  });
}

// ─── REPP recibido de proveedor (TES-FE-PR6b, §3.6.b / TES-4) ───────

/** <c>useReppPendientes(filtros)</c> — pagos PPD (o sin dato) sin complemento, con SLA 5 días. */
export function useReppPendientes(
  filtros: {
    proveedorId?: string;
    soloVencidos?: boolean;
    incluirSinMetodo?: boolean;
    offset?: number;
    limit?: number;
  } = {},
) {
  return useQuery<PagedResponse<ReppPendienteResponse>>({
    queryKey: tesoreriaKeys.reppPendientesList(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.proveedorId) params.set('proveedorId', filtros.proveedorId);
      if (filtros.soloVencidos != null)
        params.set('soloVencidos', String(filtros.soloVencidos));
      if (filtros.incluirSinMetodo != null)
        params.set('incluirSinMetodo', String(filtros.incluirSinMetodo));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<ReppPendienteResponse>>(
        qs ? `${BASE}/repp-pendientes?${qs}` : `${BASE}/repp-pendientes`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useRegistrarReppRecibido()</c> — registra el complemento del
 * proveedor (UUID único + XML base64 opcional a blob) y publica
 * repp-proveedor.recibido.v1 → CxP libera FALTA_REPP.
 */
export function useRegistrarReppRecibido() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: {
        facturaProveedorId: string;
        uuidComplemento: string;
        fechaComplemento: string;
        xmlBase64?: string;
      };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ReppRecibidoResponse>(
        `${BASE}/repp-recibidos`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.repp() });
    },
  });
}

// ─── Reportes ADR-0036 (TES-FE-PR6) ──────────────────────────────────

/** <c>useReporteFlujoEfectivo(filtros)</c> — clasificación por concepto. */
export function useReporteFlujoEfectivo(filtros: {
  desde: string;
  hasta: string;
  cuentaBancariaId?: string;
  moneda?: string;
}) {
  return useQuery<ReporteTesoreriaBackend>({
    queryKey: tesoreriaKeys.flujoEfectivo(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({
        desde: filtros.desde,
        hasta: filtros.hasta,
      });
      if (filtros.cuentaBancariaId)
        params.set('cuentaBancariaId', filtros.cuentaBancariaId);
      if (filtros.moneda) params.set('moneda', filtros.moneda);
      const { data } = await apiRequest<ReporteTesoreriaBackend>(
        `${BASE}/reportes/flujo-efectivo?${params}`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useReporteAuxiliarBancos(filtros)</c> — libro por cuenta con saldo acumulado. */
export function useReporteAuxiliarBancos(filtros: {
  cuentaBancariaId: string | null;
  desde: string;
  hasta: string;
}) {
  return useQuery<ReporteTesoreriaBackend>({
    queryKey: tesoreriaKeys.auxiliarBancos(filtros),
    enabled: filtros.cuentaBancariaId != null,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({
        cuentaBancariaId: filtros.cuentaBancariaId!,
        desde: filtros.desde,
        hasta: filtros.hasta,
      });
      const { data } = await apiRequest<ReporteTesoreriaBackend>(
        `${BASE}/reportes/auxiliar-bancos?${params}`,
        { signal },
      );
      return data;
    },
  });
}

// ─── Mutations (Idempotency-Key ADR-0020) ────────────────────────────

/** <c>useRegistrarPago()</c> — POST /pagos multi-pasivo (RN-4: aplicado.v1 por factura). */
export function useRegistrarPago() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: {
        cuentaBancariaId: string;
        fechaValor: string;
        aplicaciones: AplicacionPagoItem[];
        referenciaBancaria?: string;
        conceptoId?: string;
      };
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PagoProveedorResponse>(`${BASE}/pagos`, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all });
    },
  });
}

/** <c>useRevertirPago()</c> — POST /pagos/{pagoId}/revertir (RN-10: contramovimiento). */
export function useRevertirPago() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      pagoId: string;
      motivo: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<PagoProveedorResponse>(
        `${BASE}/pagos/${args.pagoId}/revertir`,
        {
          method: 'POST',
          body: { motivo: args.motivo },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all });
    },
  });
}

/** <c>useSolicitarCancelacionPasivo()</c> — POST hacia CxP (EnviarARevision). */
export function useSolicitarCancelacionPasivo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      facturaProveedorId: string;
      motivo: string;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `${BASE}/pasivos-pendientes/${args.facturaProveedorId}/solicitar-cancelacion`,
        {
          method: 'POST',
          body: { motivo: args.motivo },
          idempotencyKey: args.idempotencyKey,
        },
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all });
    },
  });
}
