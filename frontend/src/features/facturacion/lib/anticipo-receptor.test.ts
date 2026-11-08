import { describe, expect, it } from 'vitest';
import { paisReceptorAnticipo } from './anticipo-receptor';

describe('paisReceptorAnticipo (B4)', () => {
  it('el cliente extranjero lleva su país de residencia, no MEX', () => {
    expect(paisReceptorAnticipo('XEXX010101000', 'USA')).toBe('USA');
    expect(paisReceptorAnticipo(' xexx010101000 ', 'can')).toBe('CAN');
  });

  it('el cliente nacional y el extranjero sin país en su ficha quedan en MEX', () => {
    expect(paisReceptorAnticipo('AAA010101AAA', 'USA')).toBe('MEX');
    expect(paisReceptorAnticipo('XEXX010101000', null)).toBe('MEX');
    expect(paisReceptorAnticipo(null, undefined)).toBe('MEX');
  });
});
