import { describe, expect, it } from 'vitest';
import { formatCantidad } from '@/lib/format-cantidad';

describe('formatCantidad', () => {
  it('usa los decimales de la UM con separador de miles', () => {
    expect(formatCantidad(1234.5, 2)).toBe('1,234.50');
    expect(formatCantidad(1234, 0)).toBe('1,234');
  });
  it('sin decimales conocidos no rellena ceros', () => {
    expect(formatCantidad(12.5)).toBe('12.5');
  });
  it('nullish o NaN → guion', () => {
    expect(formatCantidad(null, 2)).toBe('—');
    expect(formatCantidad(Number.NaN)).toBe('—');
  });
});
