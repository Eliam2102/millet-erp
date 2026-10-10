import { describe, expect, it } from 'vitest';
import { estadoFiscalTexto, faltantesFicha } from './ficha-fiscal';
import { fichaFiscalSchema, clasificacionRequerida } from '../api/f1a';
describe('F1A ficha fiscal y clasificación', () => {
  it('requiere los cuatro datos sin confundir tasa cero con ausencia', () => {
    expect(faltantesFicha({ claveProdServSat: '30171706', claveUnidadSat: 'H87', objetoImp: '02', tasaIvaTraslado: 0 })).toEqual([]);
    expect(faltantesFicha({ claveProdServSat: null, claveUnidadSat: 'H87', objetoImp: null, tasaIvaTraslado: null })).toEqual(['clave de producto SAT', 'objeto de impuesto', 'tasa de IVA']);
  });
  it('acepta identificadores del seed con zId y bloquea guardar sin clasificación', () => {
    expect(clasificacionRequerida.safeParse('00000012-0001-0000-0000-000000000001').success).toBe(true);
    expect(clasificacionRequerida.safeParse('').success).toBe(false);
  });
  it('valida formato y tasas de cualquier ficha', () => {
    const ficha = { claveProdServSat: '30171706', claveUnidadSat: 'H87', objetoImp: '08', tasaIvaTraslado: 0, tasaRetencionIva: null, tasaRetencionIsr: null };
    expect(fichaFiscalSchema.safeParse(ficha).success).toBe(true);
    expect(fichaFiscalSchema.safeParse({ ...ficha, claveProdServSat: 'xxxxxxxx', tasaIvaTraslado: 1.1 }).success).toBe(false);
  });
  it('muestra los estados del backend', () => {
    expect(estadoFiscalTexto(0)).toBe('Pendiente de captura fiscal');
    expect(estadoFiscalTexto('PorReglaSinRevisar')).toBe('Por regla, sin revisar');
    expect(estadoFiscalTexto(2)).toBe('Confirmada');
  });
});

describe('historial fiscal legible', () => {
  it('traduce cambios de tasas y estados sin mostrar JSON', async () => {
    const { cambiosFiscalesTexto } = await import('./ficha-fiscal');
    expect(cambiosFiscalesTexto('{"Ficha":{"TasaIvaTraslado":0.16},"EstadoFiscal":0}', '{"Ficha":{"TasaIvaTraslado":0},"EstadoFiscal":2}')).toEqual([
      'IVA: 16 % → 0 %', 'Estado fiscal: Pendiente de captura fiscal → Confirmada',
    ]);
  });
  it('maneja registros incompletos y cambios sin valores fiscales', async () => {
    const { cambiosFiscalesTexto } = await import('./ficha-fiscal');
    expect(cambiosFiscalesTexto('null', '{}')).toEqual(['Sin cambios en los datos fiscales.']);
  });
});
