import { describe, expect, it } from 'vitest';
import { AnticipoSchema } from '@/features/facturacion/schemas/anticipo';
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
  montoBase: 1000,
  tasaIvaTraslado: 0.16,
  descripcion: null,
  obraNombre: null,
};

describe('AnticipoSchema', () => {
  it('acepta un anticipo válido', () => {
    expect(AnticipoSchema.safeParse(valido).success).toBe(true);
  });

  it('exige monto base > 0', () => {
    expect(AnticipoSchema.safeParse({ ...valido, montoBase: 0 }).success).toBe(
      false,
    );
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
