/**
 * Tipo Money del frontend. Coincide con el value object Money del backend
 * (ADR-0014): Amount + Currency. Las operaciones aritméticas viven solo en
 * el backend; el frontend solo formatea para mostrar al usuario.
 */
export interface Money {
  amount: number;
  currency: string;
}

/** Símbolos sugeridos por moneda. Cualquier moneda no listada usa solo el código ISO. */
const CURRENCY_SYMBOLS: Record<string, string> = {
  MXN: '$',
  USD: '$',
  EUR: '€',
  GBP: '£',
  JPY: '¥',
};

const DEFAULT_LOCALE = 'es-MX';

/**
 * Formatea un Money con el símbolo y separadores de miles locales.
 *
 * - MXN (moneda principal): `$1,234.56` (sin código)
 * - Otras monedas: `$1,234.56 USD`, `€1,234.56 EUR`
 *
 * Ver ADR-0014.
 */
export function formatMoney(money: Money): string {
  const decimals = decimalsFor(money.currency);
  const formatter = new Intl.NumberFormat(DEFAULT_LOCALE, {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });

  const symbol = CURRENCY_SYMBOLS[money.currency] ?? '';
  // Guard: un amount null/undefined/NaN (p. ej. un total que el backend
  // devuelve null, o un Money parcial vía cast) rendería "$NaN"/"$undefined"
  // en las bandejas, sin error. Se coerce a 0 para no mostrar dato corrupto.
  const amount = Number.isFinite(money.amount) ? money.amount : 0;
  const formatted = formatter.format(amount);
  const body = symbol ? `${symbol}${formatted}` : formatted;

  return money.currency === 'MXN' ? body : `${body} ${money.currency}`;
}

/** Decimales convencionales por moneda. JPY no tiene decimales; las demás usan 2. */
function decimalsFor(currency: string): number {
  if (currency === 'JPY') return 0;
  return 2;
}

/** Helper para construir Money desde números literales en código. */
export function money(amount: number, currency: string): Money {
  return { amount, currency };
}

/** Convenience helpers para las monedas más usadas en MX. */
export const mxn = (amount: number): Money => money(amount, 'MXN');
export const usd = (amount: number): Money => money(amount, 'USD');
