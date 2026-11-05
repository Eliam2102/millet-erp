import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type {
  AplicarAnticipoAFacturaResponse,
  BandejaScoped,
  ControlAnticipoFila,
  ControlAnticiposDetalladaResponse,
  EmitirAnticipoCommand,
  EmitirAnticipoResponse,
  FacturaAnticipoBandejaItem,
  FacturaAnticipoDetalleResponse,
  NotaCreditoDevolucionAnticipoCommand,
  NotaCreditoDevolucionAnticipoResponse,
  ReporteBackend,
} from '@/features/facturacion/api/types';

const BASE = '/api/v1/facturacion/anticipos';

export interface ControlAnticiposFiltros {
  clienteId?: string;
  estado?: number;
  obraId?: number;
  desde?: string;
  hasta?: string;
}

/**
 * <c>useControlAnticipos(filtros)</c> — Control de Anticipos (resumen).
 * El backend devuelve el shape de reporte (ADR-0036) listo para
 * <c>&lt;ReporteShell&gt;</c>.
 */
export function useControlAnticipos(filtros: ControlAnticiposFiltros) {
  return useQuery<ReporteBackend<ControlAnticipoFila>>({
    queryKey: facturacionKeys.anticiposControl(
      filtros as Record<string, unknown>,
    ),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      if (filtros.obraId != null) params.set('obraId', String(filtros.obraId));
      if (filtros.desde) params.set('desde', filtros.desde);
      if (filtros.hasta) params.set('hasta', filtros.hasta);
      const qs = params.toString();
      const { data } = await apiRequest<ReporteBackend<ControlAnticipoFila>>(
        qs ? `${BASE}/control?${qs}` : `${BASE}/control`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useEstadoCuentaAnticipos(clienteId)</c> — detallada por cliente. */
export function useEstadoCuentaAnticipos(clienteId: string | null | undefined) {
  return useQuery<ControlAnticiposDetalladaResponse>({
    queryKey:
      clienteId != null
        ? facturacionKeys.anticiposControlCliente(clienteId)
        : ['facturacion', 'noop-anticipo-cliente'],
    queryFn: async ({ signal }) => {
      if (clienteId == null) throw new Error('sin clienteId');
      const { data } = await apiRequest<ControlAnticiposDetalladaResponse>(
        `${BASE}/control/${clienteId}`,
        { signal },
      );
      return data;
    },
    enabled: clienteId != null,
    staleTime: 0,
  });
}

export interface BandejaFacturasAnticipoFiltros {
  estado?: number;
  limit?: number;
  alcance?: 'sin-asignar';
}

/**
 * <c>useBandejaFacturasAnticipo(filtros)</c> — bandeja de facturas de
 * anticipo (ANT-PR2, doc 13 §6). Filtro por estado de timbrado server-side;
 * incluye estado/saldo del agregado <c>Anticipo</c> (13-H) y el bucket
 * "Sin asignar" de la Capa A.
 */
export function useBandejaFacturasAnticipo(filtros: BandejaFacturasAnticipoFiltros) {
  return useQuery<BandejaScoped<FacturaAnticipoBandejaItem>>({
    queryKey: facturacionKeys.anticiposFacturasList(
      filtros as Record<string, unknown>,
    ),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      params.set('limit', String(filtros.limit ?? 200));
      if (filtros.alcance) params.set('alcance', filtros.alcance);
      const { data } = await apiRequest<BandejaScoped<FacturaAnticipoBandejaItem>>(
        `${BASE}/facturas?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useFacturaAnticipoDetalle(id)</c> — detalle enriquecido (13-A). */
export function useFacturaAnticipoDetalle(id: string | null | undefined) {
  return useQuery<FacturaAnticipoDetalleResponse>({
    queryKey:
      id != null
        ? facturacionKeys.anticipoFacturaById(id)
        : ['facturacion', 'noop-anticipo-factura'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useFacturaAnticipoDetalle invocado sin id');
      const { data } = await apiRequest<FacturaAnticipoDetalleResponse>(
        `${BASE}/facturas/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}

/**
 * <c>useEmitirAnticipo()</c> — emite una factura de anticipo (serie
 * FANT). Mutación con impacto fiscal → Idempotency-Key (ADR-0020). El
 * stub de timbrado devuelve UUID síncrono en dev.
 */
export function useEmitirAnticipo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: EmitirAnticipoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EmitirAnticipoResponse>(`${BASE}/`, {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.anticipos() });
      // El anticipo emitido también es una factura → refresca la bandeja.
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturas() });
    },
  });
}

/**
 * F6.3 · CA6.17: devuelve en dinero un anticipo no aplicado (NC relación 01 al
 * anticipo); al timbrar pide el reembolso a Tesorería con la cuenta destino.
 */
export function useEmitirNcDevolucionAnticipo(facturaAnticipoId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: NotaCreditoDevolucionAnticipoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<NotaCreditoDevolucionAnticipoResponse>(
        '/api/v1/facturacion/notas-credito/devolucion-anticipo',
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
        queryKey: facturacionKeys.anticipoFacturaById(facturaAnticipoId),
      });
    },
  });
}

/**
 * F1-CXC-05 P2 (compensación): aplica el saldo de un anticipo a una factura
 * ya timbrada del mismo cliente; emite la NC de amortización (relación 07).
 */
export function useAplicarAnticipoAFactura(facturaVentaId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { anticipoId: string; importe: number; comentarios?: string | null; idempotencyKey: string }) => {
      const { data } = await apiRequest<AplicarAnticipoAFacturaResponse>(
        `${BASE}/${args.anticipoId}/aplicar`,
        {
          method: 'POST',
          body: { facturaVentaId, importe: args.importe, ...(args.comentarios ? { comentarios: args.comentarios } : {}) },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: facturacionKeys.facturaById(facturaVentaId) });
      queryClient.invalidateQueries({ queryKey: facturacionKeys.anticipos() });
    },
  });
}
