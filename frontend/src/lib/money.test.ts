import { describe, expect, it } from 'vitest';
import { formatMoney, type Money } from '@/lib/money';

describe('formatMoney', () => {
  it('MXN sin código, otras monedas con código', () => {
    expect(formatMoney({ amount: 1234.56, currency: 'MXN' })).toBe('$1,234.56');
    expect(formatMoney({ amount: 1234.56, currency: 'USD' })).toBe(
      '$1,234.56 USD',
    );
    expect(formatMoney({ amount: 1234, currency: 'JPY' })).toBe('¥1,234 JPY');
  });

  it('guard: amount no finito (null/undefined/NaN) → $0.00, nunca $NaN', () => {
    // Llega por casts/backends que devuelven null pese al tipo `number`.
    const nan = { amount: NaN, currency: 'MXN' } as Money;
    const nulo = { amount: null, currency: 'MXN' } as unknown as Money;
    const undef = { amount: undefined, currency: 'USD' } as unknown as Money;
    expect(formatMoney(nan)).toBe('$0.00');
    expect(formatMoney(nulo)).toBe('$0.00');
    expect(formatMoney(undef)).toBe('$0.00 USD');
  });
});
