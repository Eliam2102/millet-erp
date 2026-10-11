import { describe, expect, it } from 'vitest';
import { fechaLocalISO } from './datetime';
describe('Fecha fiscal de México al vincular el XML', () => {
  it('conserva el día del CFDI aunque el instante UTC ya sea el día siguiente', () => {
    expect(fechaLocalISO('2026-10-11T05:30:00Z')).toBe('2026-10-10');
    expect(fechaLocalISO('2026-10-10T23:30:00-06:00')).toBe('2026-10-10');
  });
});
