import { describe, expect, it } from 'vitest';
import { formatearFecha } from './formato';

describe('formatearFecha', () => {
  it('no retrocede un día con fechas de calendario a medianoche UTC', () => {
    expect(formatearFecha('2026-10-09T00:00:00+00:00')).toBe('09/10/2026');
    expect(formatearFecha('2026-10-09')).toBe('09/10/2026');
  });
});
