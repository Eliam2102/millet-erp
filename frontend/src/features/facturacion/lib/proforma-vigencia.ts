import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';

/** DD/MM/AAAA de una fecha AAAA-MM-DD, sin pasar por la zona horaria del navegador. */
export function fechaCorta(isoFecha: string): string {
  const [a, m, d] = isoFecha.split('-');
  return `${d}/${m}/${a}`;
}

/**
 * M-145 (86e3zr6d2): texto de la vigencia de la proforma (15 días naturales por omisión). Vacío en las proformas
 * generadas antes de registrar la vigencia.
 */
export function textoVigenciaProforma(p: Pick<ProformaPedidoResponse, 'vigenteHasta' | 'vencida'>): string {
  if (!p.vigenteHasta) return '';
  return p.vencida ? `Vencida (vigente hasta el ${fechaCorta(p.vigenteHasta)})` : `Vigente hasta el ${fechaCorta(p.vigenteHasta)}`;
}
