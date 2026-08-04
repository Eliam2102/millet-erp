import { describe, expect, it } from 'vitest';
import { esUuidValido } from './uuid';

describe('esUuidValido', () => {
  it('acepta UUID canónico en cualquier caja', () => {
    expect(esUuidValido('3fa85f64-5717-4562-b3fc-2c963f66afa6')).toBe(true);
    expect(esUuidValido(' 3FA85F64-5717-4562-B3FC-2C963F66AFA6 ')).toBe(true);
  });

  it('rechaza folios que no son UUID', () => {
    expect(esUuidValido('A-191')).toBe(false);
    expect(esUuidValido('3fa85f64-5717-4562-b3fc')).toBe(false);
    expect(esUuidValido('')).toBe(false);
  });
});
