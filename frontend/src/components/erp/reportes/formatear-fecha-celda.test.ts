import { describe, expect, it } from 'vitest';
import { formatearFechaCelda } from '@/components/erp/reportes/formatear-fecha-celda';

describe('formatearFechaCelda', () => {
  it('una fecha de calendario no se recorre un día por la zona horaria', () => {
    expect(formatearFechaCelda('2026-09-30')).toBe('30/9/2026');
  });
});
