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

/**
 * 86e3tfdvt (Millet, 30-oct): del voucher de crédito (04) y débito (28) también
 * se capturan los últimos 4 dígitos de la tarjeta; nunca el número completo.
 * La 29 (servicios) no se habilita en caja y conserva solo la autorización.
 * Misma regla que el backend (COBRO_TARJETA_SIN_ULTIMOS4 / _ULTIMOS4_INVALIDOS).
 */
export const FORMAS_PAGO_CON_ULTIMOS4: readonly string[] = ['04', '28'];

const ULTIMOS_4 = /^\d{4}$/;

export const MENSAJE_ULTIMOS4 =
  'Captura los últimos 4 dígitos de la tarjeta (exactamente 4 números) en cada pago con tarjeta.';

export function pideUltimos4(clave: string): boolean {
  return FORMAS_PAGO_CON_ULTIMOS4.includes(clave);
}

/** Deja solo dígitos y a lo más 4, para el campo de captura. */
export function soloUltimos4(valor: string): string {
  return valor.replace(/\D/g, '').slice(0, 4);
}

/** true si alguna forma de crédito o débito no trae exactamente 4 dígitos de la tarjeta. */
export function faltanUltimos4(
  formas: readonly { formaPago: string; tarjetaUltimos4?: string | null }[],
): boolean {
  return formas.some(
    (f) => pideUltimos4(f.formaPago) && !ULTIMOS_4.test((f.tarjetaUltimos4 ?? '').trim()),
  );
}

/** Voucher para mostrar: «123456 · **** 1234» (o solo la autorización). */
export function textoVoucher(
  referencia: string | null | undefined,
  tarjetaUltimos4: string | null | undefined,
): string | null {
  if (!referencia && !tarjetaUltimos4) return null;
  if (!tarjetaUltimos4) return referencia ?? null;
  return referencia ? `${referencia} · **** ${tarjetaUltimos4}` : `**** ${tarjetaUltimos4}`;
}
