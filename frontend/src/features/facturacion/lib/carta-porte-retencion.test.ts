import { describe, expect, it } from 'vitest';
import { tasaRetencionDe, totalesCartaPorte } from './carta-porte-retencion';

describe('totalesCartaPorte (P51)', () => {
  it('tipo I con retención de fletes resta el 4 % del total', () => {
    expect(totalesCartaPorte('I', 5000, 0.16, 'FLETES')).toEqual({ iva: 800, retencion: 200, total: 5600 });
  });

  it('tipo I con servicios retiene 6 %', () => {
    expect(totalesCartaPorte('I', 5000, 0.16, 'SERVICIOS')).toEqual({ iva: 800, retencion: 300, total: 5500 });
  });

  it('tipo I sin retención conserva monto + IVA', () => {
    expect(totalesCartaPorte('I', 5000, 0.16, null)).toEqual({ iva: 800, retencion: 0, total: 5800 });
  });

  it('redondea a centavos como el backend', () => {
    expect(totalesCartaPorte('I', 1234.56, 0.16, 'FLETES')).toEqual({ iva: 197.53, retencion: 49.38, total: 1382.71 });
  });

  it('tipo T no lleva importes aunque traiga concepto', () => {
    expect(totalesCartaPorte('T', 5000, 0.16, 'FLETES')).toEqual({ iva: 0, retencion: 0, total: 0 });
  });

  it('tasaRetencionDe usa el catálogo de la factura de servicio', () => {
    expect(tasaRetencionDe('FLETES')).toBe(0.04);
    expect(tasaRetencionDe('DESPERDICIO')).toBe(0.16);
    expect(tasaRetencionDe('OTRO')).toBeNull();
    expect(tasaRetencionDe(null)).toBeNull();
  });
});
