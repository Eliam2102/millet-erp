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

/**
 * c_FormaPago de tarjeta (04 crédito, 28 débito, 29 servicios): exigen el
 * número de autorización del voucher de la terminal (FIX-12, 86e3tfdvt).
 */
export const FORMAS_PAGO_TARJETA: readonly string[] = ['04', '28', '29'];

/**
 * 86e3tfdvt: número de autorización del voucher, 4 a 12 letras, dígitos o
 * guiones. El tope impide capturar el número completo de la tarjeta (13–19
 * dígitos), que nunca se guarda. Misma regla que el backend
 * (COBRO_TARJETA_AUTORIZACION_INVALIDA).
 */
const AUTORIZACION_VOUCHER = /^[A-Za-z0-9-]{4,12}$/;

/** Mensaje único para el cajero cuando el voucher falta o no es válido. */
export const MENSAJE_VOUCHER =
  'Captura el número de autorización del voucher (4 a 12 letras o dígitos, nunca el número de la tarjeta) en cada pago con tarjeta.';

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

/** true si alguna forma de tarjeta trae un número de autorización con formato inválido. */
export function autorizacionVoucherInvalida(
  formas: readonly { formaPago: string; referencia?: string | null }[],
): boolean {
  return formas.some((f) => {
    const valor = (f.referencia ?? '').trim();
    return esFormaTarjeta(f.formaPago) && valor !== '' && !AUTORIZACION_VOUCHER.test(valor);
  });
}

/** true si el voucher de alguna forma de tarjeta falta o es inválido (bloquea el cobro). */
export function voucherIncompleto(
  formas: readonly { formaPago: string; referencia?: string | null }[],
): boolean {
  return faltaVoucherTarjeta(formas) || autorizacionVoucherInvalida(formas);
}
