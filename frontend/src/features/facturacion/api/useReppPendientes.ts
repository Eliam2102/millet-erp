import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

export type EstadoPendiente = 'Pendiente' | 'Emitido' | 'Descartado';
export interface RelacionRepp {
  facturaVentaId: string;
  importe: number;
  folio?: string | null;
}
export interface ReppPendiente {
  id: string;
  clienteId: string;
  clienteNombre: string;
  clienteRfc: string | null;
  movimientoBancarioId: string;
  cuentaBancariaId: string;
  propuestaId: string | null;
  monto: number;
  moneda: string;
  fechaValor: string;
  fechaLimite: string;
  referencia: string | null;
  formaPago: string;
  revisado: boolean;
  estado: EstadoPendiente;
  alerta: string;
  tipoCambio: number | null;
  tcPorRegistrar: boolean;
  reciboPagoId: string | null;
  intentoReciboPagoId: string | null;
  ultimoErrorCodigo: string | null;
  ultimoErrorMensaje: string | null;
  motivoDescarte: string | null;
  facturas: RelacionRepp[];
  bloqueos: string[];
  /** 86e3tfdve: cuenta de Millet del depósito («Banco · •••• 1234»). */
  cuentaBancariaEtiqueta?: string | null;
  /** 86e3tfdve: beneficiaria que se usará al emitir con la forma guardada (enmascarada). */
  cuentaBeneficiariaSugerida?: string | null;
  /** 86e3tfdve (opción A): ordenante guardada, la del ingreso o la corregida al revisar (enmascarada). */
  cuentaOrdenante?: string | null;
}
/**
 * 86e3tfdve: cuentas del pago. La ordenante capturada reemplaza a la guardada
 * (vacía = conservarla; `limpiarCuentaOrdenante` la quita); la beneficiaria no se guarda.
 */
export interface CuentasPago {
  cuentaOrdenante: string | null;
  cuentaBeneficiaria: string | null;
  limpiarCuentaOrdenante?: boolean;
}
export interface PendientesResponse {
  items: ReppPendiente[];
  total: number;
  kpis: { pendientes: number; cercaDelPlazo: number; vencidos: number; conError: number };
}
export interface EmisionPendiente {
  id: string;
  emitido: boolean;
  reciboPagoId: string | null;
  codigo: string | null;
  mensaje: string | null;
  /** 86e3tfdve: en lote, el REP salió sin la ordenante guardada porque no cumple la forma de pago. */
  aviso?: string | null;
}
const BASE = '/api/v1/facturacion/repp/pendientes';
const KEY = ['facturacion', 'repp-pendientes'] as const;

export function useReppPendientes(estado: EstadoPendiente | '', indicador: string, offset: number) {
  return useQuery({
    queryKey: [...KEY, 'lista', estado, indicador, offset],
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ indicador, offset: String(offset), limit: '10' });
      if (estado) params.set('estado', estado);
      return (await apiRequest<PendientesResponse>(`${BASE}?${params}`, { signal })).data;
    },
  });
}
export function useReppPendiente(id: string | null) {
  return useQuery({
    queryKey: [...KEY, id],
    enabled: id !== null,
    queryFn: async ({ signal }) =>
      (await apiRequest<ReppPendiente>(`${BASE}/${id}`, { signal })).data,
  });
}
export function useAccionesReppPendiente() {
  const client = useQueryClient();
  const refrescar = () => client.invalidateQueries({ queryKey: ['facturacion'] });
  const revisar = useMutation({
    mutationFn: async ({
      id,
      formaPago,
      facturas,
      cuentas,
    }: {
      id: string;
      formaPago: string;
      facturas: RelacionRepp[];
      cuentas?: CuentasPago;
    }) =>
      apiRequest(`${BASE}/${id}`, { method: 'PUT', body: { formaPago, facturas, ...cuentas } }),
    onSettled: refrescar,
  });
  const emitir = useMutation({
    mutationFn: async ({ ids, idempotencyKey }: { ids: string[]; idempotencyKey: string }) =>
      (
        await apiRequest<EmisionPendiente[]>(`${BASE}/emitir-lote`, {
          method: 'POST',
          body: { ids },
          idempotencyKey,
        })
      ).data,
    onSettled: refrescar,
  });
  const emitirUno = useMutation({
    mutationFn: async ({
      id,
      idempotencyKey,
      cuentas,
    }: {
      id: string;
      idempotencyKey: string;
      cuentas?: CuentasPago;
    }) =>
      (
        await apiRequest<EmisionPendiente>(`${BASE}/${id}/emitir`, {
          method: 'POST',
          body: cuentas,
          idempotencyKey,
        })
      ).data,
    onSettled: refrescar,
  });
  const descartar = useMutation({
    mutationFn: async ({ id, motivo }: { id: string; motivo: string }) =>
      apiRequest(`${BASE}/${id}/descartar`, { method: 'POST', body: { motivo } }),
    onSettled: refrescar,
  });
  return { revisar, emitir, emitirUno, descartar };
}
