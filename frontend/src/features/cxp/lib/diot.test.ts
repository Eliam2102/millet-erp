import { describe, expect, it } from 'vitest';
import { mesAnteriorIso } from './diot';

describe('mesAnteriorIso', () => {
  it('regresa el mes anterior', () => {
    expect(mesAnteriorIso('2026-11-07')).toBe('2026-10');
  });
  it('en enero regresa diciembre del año anterior', () => {
    expect(mesAnteriorIso('2027-01-15')).toBe('2026-12');
  });
});
