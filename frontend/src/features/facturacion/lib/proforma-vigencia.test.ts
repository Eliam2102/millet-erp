import { describe, expect, it } from 'vitest';
import { fechaCorta, textoVigenciaProforma } from '@/features/facturacion/lib/proforma-vigencia';

describe('vigencia de la proforma (M-145)', () => {
  it('formatea la fecha sin correrla por la zona horaria', () => {
    expect(fechaCorta('2026-11-05')).toBe('05/11/2026');
  });
  it('dice hasta cuándo vale, si ya venció, y nada en proformas anteriores', () => {
    expect(textoVigenciaProforma({ vigenteHasta: '2026-11-05', vencida: false })).toBe('Vigente hasta el 05/11/2026');
    expect(textoVigenciaProforma({ vigenteHasta: '2026-11-05', vencida: true })).toBe('Vencida (vigente hasta el 05/11/2026)');
    expect(textoVigenciaProforma({ vigenteHasta: null })).toBe('');
  });
});
