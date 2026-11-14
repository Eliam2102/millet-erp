import type { EstadoRedondeoPago, RedondeoPagoResponse } from '../api/types';

/** M-326 (b): tope del redondeo de un pago a proveedor, en MXN (±). */
export const TOLERANCIA_REDONDEO_PAGO = 0.99;

const r2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100;

/** Redondeo que implica pagar `importe` contra `saldo`: importe + redondeo = saldo (positivo = se pagó menos). */
export function redondeoDePago(saldo: number, importe: number): number {
  return r2(saldo - importe);
}

export type ClaseImporte = 'exacto' | 'redondeo' | 'invalido';

/**
 * Clasifica el importe capturado. Solo las facturas de proveedor en MXN admiten redondeo; en otra moneda o
 * en pasivos internos el pago es por el saldo o menos (pago parcial), como antes.
 */
export function clasificarImporte(saldo: number, importe: number, admiteRedondeo: boolean): ClaseImporte {
  if (!Number.isFinite(importe) || importe <= 0) return 'invalido';
  const r = redondeoDePago(saldo, importe);
  if (admiteRedondeo && r !== 0 && Math.abs(r) <= TOLERANCIA_REDONDEO_PAGO) return 'redondeo';
  return importe <= saldo ? 'exacto' : 'invalido';
}

export type EstadoRedondeoUi = 'sin-solicitud' | EstadoRedondeoPago;

export interface SituacionRedondeo {
  estado: EstadoRedondeoUi;
  solicitud: RedondeoPagoResponse | null;
  /** El pago puede salir: hay autorización vigente por el mismo monto. */
  autorizado: boolean;
}

/** Toma la solicitud vigente de la factura (solicitada o autorizada); si no hay, la rechazada más reciente. */
export function situacionRedondeo(
  solicitudes: readonly RedondeoPagoResponse[],
  redondeo: number,
): SituacionRedondeo {
  const vigente = solicitudes.find((s) => s.estado === 'Solicitada' || s.estado === 'Autorizada');
  const solicitud = vigente ?? [...solicitudes].reverse().find((s) => s.estado === 'Rechazada') ?? null;
  if (!solicitud) return { estado: 'sin-solicitud', solicitud: null, autorizado: false };
  return {
    estado: solicitud.estado,
    solicitud,
    autorizado: solicitud.estado === 'Autorizada' && r2(solicitud.redondeo) === r2(redondeo),
  };
}

/** Texto del estado para el cajero. */
export function textoSituacionRedondeo(s: SituacionRedondeo, redondeo: number): string {
  switch (s.estado) {
    case 'sin-solicitud':
      return 'Requiere autorización del Gerente de Área.';
    case 'Solicitada':
      return 'Solicitud enviada: espera la autorización del Gerente de Área.';
    case 'Autorizada':
      return s.autorizado
        ? 'Autorizado por el Gerente de Área.'
        : `La autorización vigente es por ${s.solicitud?.redondeo.toFixed(2)}; el redondeo de este pago es ${redondeo.toFixed(2)}.`;
    case 'Rechazada':
      return `Rechazado: ${s.solicitud?.motivoRechazo ?? 'sin motivo'}. Ajusta el importe o solicita de nuevo.`;
    default:
      return 'Esta autorización ya se usó en otro pago.';
  }
}

/** La lista de la API puede venir como arreglo o paginada (`items`). */
export function normalizarRedondeos(data: unknown): RedondeoPagoResponse[] {
  if (Array.isArray(data)) return data as RedondeoPagoResponse[];
  const items = (data as { items?: unknown } | null)?.items;
  return Array.isArray(items) ? (items as RedondeoPagoResponse[]) : [];
}
