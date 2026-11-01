import { fechaLocalISO } from '@/lib/datetime';

/** La fecha del documento puede llegar como día de negocio a medianoche UTC (captura manual) o como
 * instante real del CFDI; en ambos casos se compara contra el día que ve el usuario. */
export function esContado(fechaVencimiento: string, fechaDocumento: string): boolean {
  return fechaVencimiento === fechaDocumento.slice(0, 10) || fechaVencimiento === fechaLocalISO(fechaDocumento);
}
