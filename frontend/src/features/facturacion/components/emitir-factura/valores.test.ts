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

describe('valoresIniciales — M-158: datos aduanales de Comercio Exterior', () => {
  const embarque = {
    ligado: true, expedienteId: 'e-1', sucursalId: null, listoParaFacturar: true, codigoBloqueo: null, motivo: null,
    folioExpediente: 'CE-1', numeroPedimento: 'P-1', clavePedimento: 'A1', fechaPedimento: '2026-11-01', incoterm: 'FOB',
    contenedores: null,
    datosAduanales: [
      { posicion: 2, fraccionArancelaria: '7007199900', unidadAduana: '06' },
      { posicion: 1, fraccionArancelaria: '7610100100', unidadAduana: '01' },
    ],
  };

  it('con expediente ligado, la fracción y la unidad de CE mandan sobre las del artículo, por posición', () => {
    const prefill: EmitirFacturaPrefill = {
      embarqueCe: embarque,
      lineas: [
        { ...linea, fraccionArancelaria: '76101001', unidadAduana: '06' },
        { ...linea, fraccionArancelaria: null, unidadAduana: null },
      ],
    };
    const [l1, l2] = valoresIniciales(prefill, emisor).lineas;
    expect(l1.fraccionArancelaria).toBe('7610100100');
    expect(l1.unidadAduana).toBe('01');
    expect(l2.fraccionArancelaria).toBe('7007199900');
  });

  it('sin expediente ligado se conserva lo del artículo (B12)', () => {
    const prefill: EmitirFacturaPrefill = {
      embarqueCe: { ...embarque, ligado: false },
      lineas: [{ ...linea, fraccionArancelaria: '7610100100', unidadAduana: '06' }],
    };
    expect(valoresIniciales(prefill, emisor).lineas[0].fraccionArancelaria).toBe('7610100100');
  });
});
