import { describe, expect, it } from 'vitest';
import { AnticipoSchema, desglosarTotalAnticipo } from '@/features/facturacion/schemas/anticipo';
import { TipoAnticipo } from '@/features/facturacion/api/types';

const valido = {
  sucursalId: '11111111-1111-4111-8111-111111111111',
  clienteId: '22222222-2222-4222-8222-222222222222',
  receptorRfc: 'AAA010101AAA',
  receptorNombre: 'Cliente Demo SA',
  receptorRegimenFiscal: '601',
  receptorCodigoPostal: '97000',
  receptorUsoCfdi: 'G03',
  receptorPais: 'MEX',
  rfcEmisor: 'BBB010101BBB',
  regimenFiscalEmisor: '601',
  metodoPago: 'PUE',
  formaPago: '03',
  moneda: 'MXN',
  tipoCambio: null,
  tipoAnticipo: TipoAnticipo.ClientesMxp,
  total: 1160,
  tasaIvaTraslado: 0.16,
  descripcion: null,
  obraNombre: null,
};

describe('AnticipoSchema', () => {
  it('acepta un anticipo válido', () => {
    expect(AnticipoSchema.safeParse(valido).success).toBe(true);
  });

  it('exige total > 0', () => {
    expect(AnticipoSchema.safeParse({ ...valido, total: 0 }).success).toBe(
      false,
    );
  });

  it.each([
    [1160, 0.16, 1000, 160],
    [1000, 0.16, 862.07, 137.93],
    [1000.03, 0.16, 862.09, 137.94],
    [1000.25, 0.16, 862.28, 137.97],
    [500, 0.08, 462.96, 37.04],
    [1160, 0, 1160, 0],
    [1160, null, 1160, 0],
  ])('desglosa %s con tasa %s en base %s + IVA %s', (total, tasa, base, iva) => {
    expect(desglosarTotalAnticipo(total, tasa)).toEqual({ base, iva });
  });

  it('exige clienteId GUID (anticipo nominal)', () => {
    expect(
      AnticipoSchema.safeParse({ ...valido, clienteId: 'XAXX010101000' }).success,
    ).toBe(false);
  });

  it('rechaza tipo de anticipo fuera del enum', () => {
    expect(AnticipoSchema.safeParse({ ...valido, tipoAnticipo: 9 }).success).toBe(
      false,
    );
  });

  it('exige moneda de 3 caracteres', () => {
    expect(AnticipoSchema.safeParse({ ...valido, moneda: 'PESO' }).success).toBe(
      false,
    );
  });
});

describe('AnticipoSchema · número de pedido de A+W (86e3tfd6n)', () => {
  it('es opcional', () => {
    expect(AnticipoSchema.safeParse({ ...valido, pedidoOrigenRef: null }).success).toBe(true);
    expect(AnticipoSchema.safeParse({ ...valido, pedidoOrigenRef: '' }).success).toBe(true);
  });

  it('acepta solo dígitos', () => {
    expect(AnticipoSchema.safeParse({ ...valido, pedidoOrigenRef: '10425068' }).success).toBe(true);
    const r = AnticipoSchema.safeParse({ ...valido, pedidoOrigenRef: '10425068-005' });
    expect(r.success).toBe(false);
    expect(r.error?.issues[0]?.path).toEqual(['pedidoOrigenRef']);
  });
});
