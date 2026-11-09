import { describe, expect, it } from 'vitest';
import type { EmisorDefaultsResponse } from '@/features/facturacion/api/types';
import type { EmitirFacturaPrefill } from './prefill';
import { valoresIniciales } from './valores';

const emisor: EmisorDefaultsResponse = {
  rfcEmisor: 'AAA010101AAA',
  razonSocialEmisor: 'Emisor demo',
  regimenFiscalEmisor: '601',
  sucursalIdDefault: null,
  tasaIvaDefault: 0.16,
  codigoPostalEmisor: '77524',
};

const linea = {
  productoId: 'p-1',
  claveProdServSat: '30171505',
  descripcion: 'Puerta de aluminio y vidrio',
  claveUnidadSat: 'H87',
  cantidad: 1,
  valorUnitario: 100,
  descuento: 0,
  requierePedimento: false,
};

describe('valoresIniciales — B12: datos de aduana del artículo', () => {
  it('la línea del pedido hereda fracción, unidad aduana y peso del artículo', () => {
    const prefill: EmitirFacturaPrefill = {
      lineas: [{ ...linea, fraccionArancelaria: '7610100100', unidadAduana: '06', pesoUnitarioKg: 42.5 }],
    };
    const [l] = valoresIniciales(prefill, emisor).lineas;
    expect(l.fraccionArancelaria).toBe('7610100100');
    expect(l.unidadAduana).toBe('06');
    expect(l.pesoUnitarioKg).toBe(42.5);
  });

  it('sin datos del artículo la línea queda vacía para capturarse a mano', () => {
    const [l] = valoresIniciales({ lineas: [linea] }, emisor).lineas;
    expect(l.fraccionArancelaria).toBeNull();
    expect(l.unidadAduana).toBeNull();
    expect(l.pesoUnitarioKg).toBeNull();
  });
});
