import type { FormaPagoItem } from '@/modules/catalogos/api/types';

export function formasPagoCaja(catalogo: readonly FormaPagoItem[]): FormaPagoItem[] {
  return catalogo.filter((forma) => forma.activa);
}

export function formasPagoVigentes(
  claves: readonly string[],
  disponibles: readonly FormaPagoItem[],
): boolean {
  return (
    claves.length > 0 &&
    claves.every((clave) => disponibles.some((forma) => forma.activa && forma.claveSat === clave))
  );
}

/** c_FormaPago de tarjeta (04 crédito, 28 débito): exigen el voucher de la terminal (FIX-12). */
export const FORMAS_PAGO_TARJETA: readonly string[] = ['04', '28'];

export function esFormaTarjeta(clave: string): boolean {
  return FORMAS_PAGO_TARJETA.includes(clave);
}

/**
 * true si alguna forma de tarjeta viene sin referencia del voucher: el corte
 * de caja adjunta los comprobantes de la terminal (Manual de Sucursales §2.4)
 * y el backend rechaza el cobro (COBRO_TARJETA_SIN_REFERENCIA).
 */
export function faltaVoucherTarjeta(
  formas: readonly { formaPago: string; referencia?: string | null }[],
): boolean {
  return formas.some((f) => esFormaTarjeta(f.formaPago) && (f.referencia ?? '').trim() === '');
}
