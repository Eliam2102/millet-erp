import { describe, expect, it } from 'vitest';
import { renderCelda } from '@/components/erp/reportes/render-celda';

describe('renderCelda', () => {
  it('una fecha yyyy-MM-dd no se recorre un día', () => {
    expect(renderCelda('2026-03-01', 'fecha')).toBe(new Date('2026-03-01T12:00:00Z').toLocaleDateString('es-MX', { timeZone: 'UTC' }));
  });

  it('null se pinta como guion largo', () => {
    expect(renderCelda(null, 'moneda')).toBe('—');
  });
});
