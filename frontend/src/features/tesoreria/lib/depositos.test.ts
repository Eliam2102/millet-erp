import { describe, expect, it } from 'vitest';
import { origenDeposito } from './depositos';

describe('origenDeposito', () => {
  it('propuesta de CxC gana la etiqueta', () => {
    expect(
      origenDeposito({ propuestaCxcId: 'abc', cajaSesionId: null }),
    ).toBe('Propuesta CxC');
  });

  it('expectativa de caja sin propuesta', () => {
    expect(
      origenDeposito({ propuestaCxcId: null, cajaSesionId: 'xyz' }),
    ).toBe('Caja');
  });

  it('sin origen (defensivo, el backend lo impide con check constraint)', () => {
    expect(origenDeposito({ propuestaCxcId: null, cajaSesionId: null })).toBe('—');
  });
});
