import { describe, expect, it } from 'vitest';
import { CartaPorteSchema } from '@/features/facturacion/schemas/carta-porte';

const valido = {
  tipoCfdi: 'T' as const,
  sucursalId: '11111111-1111-4111-8111-111111111111',
  receptorRfc: 'AAA010101AAA',
  receptorNombre: 'Cliente Demo',
  receptorRegimenFiscal: '601',
  receptorCodigoPostal: '97000',
  receptorUsoCfdi: 'S01',
  receptorPais: 'MEX',
  rfcEmisor: 'BBB010101BBB',
  regimenFiscalEmisor: '601',
  moneda: 'MXN',
  origen: 'Cancún',
  destino: 'Mérida',
  origenCodigoPostal: '77500',
  origenEstado: 'ROO',
  destinoCodigoPostal: '97000',
  destinoEstado: 'YUC',
  distanciaKm: 300,
  vehiculoId: '22222222-2222-4222-8222-222222222222',
  operadorId: '33333333-3333-4333-8333-333333333333',
  fechaSalida: '2026-05-30',
  fechaLlegadaEstimada: '2026-05-30',
  montoServicio: 0,
  tasaIvaServicio: null,
  mercancias: [
    {
      descripcion: 'Vidrio',
      bienesTransp: '43211503',
      claveUnidad: 'KGM',
      cantidad: 10,
      pesoEnKg: 500,
      materialPeligroso: false,
    },
  ],
};

describe('CartaPorteSchema', () => {
  it('acepta una Carta Porte válida (tipo T)', () => {
    expect(CartaPorteSchema.safeParse(valido).success).toBe(true);
  });

  it('exige al menos una mercancía', () => {
    expect(CartaPorteSchema.safeParse({ ...valido, mercancias: [] }).success).toBe(
      false,
    );
  });

  it('exige GUID de vehículo y operador', () => {
    expect(CartaPorteSchema.safeParse({ ...valido, vehiculoId: 'x' }).success).toBe(
      false,
    );
    expect(CartaPorteSchema.safeParse({ ...valido, operadorId: 'x' }).success).toBe(
      false,
    );
  });

  it('rechaza tipo de CFDI fuera de T/I', () => {
    expect(
      CartaPorteSchema.safeParse({ ...valido, tipoCfdi: 'X' }).success,
    ).toBe(false);
  });

  it('exige distancia > 0', () => {
    expect(CartaPorteSchema.safeParse({ ...valido, distanciaKm: 0 }).success).toBe(
      false,
    );
  });

  it('exige CP de 5 dígitos y clave c_Estado de 3 letras (F12-PR3)', () => {
    expect(
      CartaPorteSchema.safeParse({ ...valido, origenCodigoPostal: '775' }).success,
    ).toBe(false);
    expect(
      CartaPorteSchema.safeParse({ ...valido, destinoEstado: 'YUCA' }).success,
    ).toBe(false);
  });
});
