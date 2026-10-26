import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';
import type {
  AnticipoSaldoClienteItem,
  EnviarEstadoCuentaArgs,
  EstadoCuentaEnvioItem,
  PendientesEstadoCuentaResponse,
  ReporteCxcBackend,
} from '@/features/cxc/api/types';

const BASE = '/api/v1/cuentas-por-cobrar';

export interface AntiguedadFiltros {
  /** "yyyy-MM-dd"; omitido = hoy (default backend). */
  fechaCorte?: string;
  clienteId?: string;
  moneda?: string;
}

/**
 * <c>useAntiguedadSaldos(filtros)</c> — antigüedad de saldos (CXC-PR6,
 * ADR-0036). Los buckets de días vencidos son CONFIGURABLES en backend
 * (<c>CuentasPorCobrar:Reportes:BucketLimites</c>) — las columnas
 * llegan dinámicas y el FE nunca las hardcodea.
 */
export function useAntiguedadSaldos(
  filtros: AntiguedadFiltros = {},
  opts: { enabled?: boolean } = {},
) {
  return useQuery<ReporteCxcBackend>({
    queryKey: cxcKeys.antiguedad({
      fechaCorte: filtros.fechaCorte ?? null,
      clienteId: filtros.clienteId ?? null,
      moneda: filtros.moneda ?? null,
    }),
    enabled: opts.enabled ?? true,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.fechaCorte) params.set('fechaCorte', filtros.fechaCorte);
      if (filtros.clienteId) params.set('clienteId', filtros.clienteId);
      if (filtros.moneda) params.set('moneda', filtros.moneda);
      const qs = params.toString();
      const { data } = await apiRequest<ReporteCxcBackend>(
        qs ? `${BASE}/cartera/antiguedad?${qs}` : `${BASE}/cartera/antiguedad`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useEstadoCuentaCliente(clienteId)</c> — estado de cuenta ADR-0036
 * (facturas, pagos, NCs y saldo corriente del cliente).
 */
export function useEstadoCuentaCliente(clienteId: string | null) {
  return useQuery<ReporteCxcBackend>({
    queryKey: cxcKeys.estadoCuenta(clienteId ?? ''),
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ReporteCxcBackend>(
        `${BASE}/cartera/estado-cuenta/${clienteId}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useAnticiposCliente(clienteId)</c> — saldos de anticipo del read
 * port de Facturación (§6.1 del 01-diseño). El cliente es obligatorio
 * en el endpoint; sin él la query no se dispara.
 */
export function useAnticiposCliente(clienteId: string | null) {
  return useQuery<AnticipoSaldoClienteItem[]>({
    queryKey: cxcKeys.anticipos(clienteId ?? ''),
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<AnticipoSaldoClienteItem[]>(
        `${BASE}/anticipos?clienteId=${clienteId}`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>usePendientesEstadoCuenta(clienteId)</c> — pagos que impiden emitir
 * el estado de cuenta (F1-CXC-09 · C7.8, CA7.14): sin REP o con REP aún
 * no reflejado en cartera. También trae el correo del cliente para
 * prellenar el envío.
 */
export function usePendientesEstadoCuenta(clienteId: string | null) {
  return useQuery<PendientesEstadoCuentaResponse>({
    queryKey: cxcKeys.estadoCuentaPendientes(clienteId ?? ''),
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<PendientesEstadoCuentaResponse>(
        `${BASE}/cartera/estado-cuenta/${clienteId}/pendientes`,
        { signal },
      );
      return data;
    },
  });
}

/** <c>useEnviosEstadoCuenta(clienteId)</c> — historial de envíos (C7.8), más reciente primero. */
export function useEnviosEstadoCuenta(clienteId: string | null) {
  return useQuery<EstadoCuentaEnvioItem[]>({
    queryKey: cxcKeys.estadoCuentaEnvios(clienteId ?? ''),
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<EstadoCuentaEnvioItem[]>(
        `${BASE}/cartera/estado-cuenta/${clienteId}/envios`,
        { signal },
      );
      return data;
    },
  });
}

/**
 * <c>useEnviarEstadoCuenta()</c> — POST multipart del PDF generado en el
 * navegador (ADR-0036) con destinatario y mensaje. Cada envío lleva su
 * propia Idempotency-Key (ADR-0020). El backend recalcula el bloqueo y
 * responde 422 <c>ESTADO_CUENTA_REPP_PENDIENTE</c> si apareció un pago sin REP.
 */
export function useEnviarEstadoCuenta() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: EnviarEstadoCuentaArgs) => {
      const formData = new FormData();
      formData.append('destinatario', args.destinatario);
      if (args.mensaje) formData.append('mensaje', args.mensaje);
      formData.append('pdf', args.pdf, args.nombreArchivo);
      const { data } = await apiRequest<EstadoCuentaEnvioItem>(
        `${BASE}/cartera/estado-cuenta/${args.clienteId}/envios`,
        { method: 'POST', body: formData, idempotencyKey: crypto.randomUUID() },
      );
      return data;
    },
    onSettled: (_data, _error, vars) => {
      queryClient.invalidateQueries({ queryKey: cxcKeys.estadoCuentaEnvios(vars.clienteId) });
      queryClient.invalidateQueries({ queryKey: cxcKeys.estadoCuentaPendientes(vars.clienteId) });
    },
  });
}
