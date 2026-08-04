import { describe, expect, it } from 'vitest';
import {
  EmitirFacturaSchema,
  EmitirFacturaLineaSchema,
} from '@/features/facturacion/schemas/emitir-factura';
import { ComportamientoFiscal } from '@/features/facturacion/api/types';

const lineaValida = {
  productoId: null,
  claveProdServSat: '01010101',
  descripcion: 'Vidrio templado',
  claveUnidadSat: 'H87',
  cantidad: 1,
  valorUnitario: 100,
  descuento: 0,
  objetoImp: '02',
  tasaIvaTraslado: 0.16,
  tasaRetencionIva: null,
  tasaRetencionIsr: null,
  requierePedimento: false,
  fraccionArancelaria: null,
  unidadAduana: null,
  cantidadAduana: null,
  valorUnitarioAduana: null,
  valorDolares: null,
  aplicaIva0: false,
  pesoUnitarioKg: null,
};

const facturaValida = {
  sucursalId: '11111111-1111-4111-8111-111111111111',
  rfcEmisor: 'AAA010101AAA',
  regimenFiscalEmisor: '601',
  receptorRfc: 'XAXX010101000',
  receptorNombre: 'Público en general',
  receptorRegimenFiscal: '616',
  receptorCodigoPostal: '97000',
  receptorUsoCfdi: 'S01',
  receptorPais: 'MEX',
  metodoPago: 'PUE',
  formaPago: '01',
  moneda: 'MXN',
  tipoCambio: null,
  // Id del catálogo compartido.canales_venta (FAC-ING-PR3).
  canalVenta: 1,
  comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
  obraNombre: null,
  cceTipoOperacion: null,
  cceIncoterm: null,
  cceTcDof: null,
  cceReceptorNumRegIdTrib: null,
  cceReceptorPaisResidencia: null,
  cceClaveDePedimento: null,
  cceCertificadoOrigen: false,
  cceReceptorDomicilioCalle: null,
  cceReceptorDomicilioEstado: null,
  cceReceptorDomicilioCodigoPostal: null,
  lineas: [lineaValida],
};

describe('EmitirFacturaLineaSchema', () => {
  it('acepta una línea válida', () => {
    expect(EmitirFacturaLineaSchema.safeParse(lineaValida).success).toBe(true);
  });

  it('exige clave SAT y unidad SAT (requeridas en emisión)', () => {
    expect(
      EmitirFacturaLineaSchema.safeParse({ ...lineaValida, claveProdServSat: '' })
        .success,
    ).toBe(false);
    expect(
      EmitirFacturaLineaSchema.safeParse({ ...lineaValida, claveUnidadSat: '' })
        .success,
    ).toBe(false);
  });

  it('acepta tasas nulas', () => {
    const r = EmitirFacturaLineaSchema.safeParse({
      ...lineaValida,
      tasaIvaTraslado: null,
    });
    expect(r.success).toBe(true);
  });
});

describe('EmitirFacturaSchema', () => {
  it('acepta una factura válida', () => {
    expect(EmitirFacturaSchema.safeParse(facturaValida).success).toBe(true);
  });

  it('exige al menos una línea', () => {
    const r = EmitirFacturaSchema.safeParse({ ...facturaValida, lineas: [] });
    expect(r.success).toBe(false);
  });

  it('exige moneda de 3 caracteres', () => {
    const r = EmitirFacturaSchema.safeParse({ ...facturaValida, moneda: 'PESO' });
    expect(r.success).toBe(false);
  });

  it('exige RFC receptor con longitud mínima', () => {
    const r = EmitirFacturaSchema.safeParse({
      ...facturaValida,
      receptorRfc: 'X',
    });
    expect(r.success).toBe(false);
  });

  it('rechaza canal de venta no positivo (id de catálogo, FAC-ING-PR3)', () => {
    expect(
      EmitirFacturaSchema.safeParse({ ...facturaValida, canalVenta: 0 })
        .success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse({ ...facturaValida, canalVenta: 2.5 })
        .success,
    ).toBe(false);
  });
});

describe('EmitirFacturaSchema — CCE (exportación)', () => {
  const facturaCce = {
    ...facturaValida,
    comportamientoFiscal: ComportamientoFiscal.ExportacionConCce,
    cceTipoOperacion: '2',
    cceIncoterm: 'FOB',
    cceTcDof: 17.5,
    cceReceptorNumRegIdTrib: 'US123456',
    cceReceptorPaisResidencia: 'USA',
    // F12-PR3 (CCE 2.0): domicilio del receptor extranjero — estado y CP
    // son obligatorios para timbrar.
    cceClaveDePedimento: 'A1',
    cceCertificadoOrigen: true,
    cceReceptorDomicilioCalle: '123 Main St',
    cceReceptorDomicilioEstado: 'Texas',
    cceReceptorDomicilioCodigoPostal: '75001',
    lineas: [
      {
        ...lineaValida,
        fraccionArancelaria: '70071100',
        unidadAduana: '06',
        cantidadAduana: 10,
        valorUnitarioAduana: 5,
        valorDolares: 50,
        aplicaIva0: true,
      },
    ],
  };

  it('acepta encabezado CCE + datos de aduana por línea', () => {
    expect(EmitirFacturaSchema.safeParse(facturaCce).success).toBe(true);
  });

  it('exige estado y CP del domicilio del receptor solo en ExportacionConCce (F12-PR3)', () => {
    expect(
      EmitirFacturaSchema.safeParse({
        ...facturaCce,
        cceReceptorDomicilioEstado: null,
      }).success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse({
        ...facturaCce,
        cceReceptorDomicilioCodigoPostal: '',
      }).success,
    ).toBe(false);
    // Fuera de exportación con CCE el domicilio no se exige.
    expect(EmitirFacturaSchema.safeParse(facturaValida).success).toBe(true);
  });

  // Datos de aduana por mercancía (#8) — espejo de ComplementoCce.AgregarLinea.
  const conLinea = (linea: Record<string, unknown>) => ({
    ...facturaCce,
    lineas: [{ ...facturaCce.lineas[0], ...linea }],
  });

  it('exige fracción arancelaria en la línea de exportación (bien tangible)', () => {
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ fraccionArancelaria: null }))
        .success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ fraccionArancelaria: '' }))
        .success,
    ).toBe(false);
  });

  it('exige fracción de 8 a 10 dígitos numéricos', () => {
    // 7 dígitos, con letras, o 11 dígitos → inválida.
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ fraccionArancelaria: '7007110' }))
        .success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ fraccionArancelaria: '7007AB00' }))
        .success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ fraccionArancelaria: '70071100' }))
        .success,
    ).toBe(true);
    // 10 dígitos también válida (fracción + NICO).
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ fraccionArancelaria: '7007110099' }))
        .success,
    ).toBe(true);
  });

  it('exige el trío unidad/cantidad/valor unitario y valor USD > 0', () => {
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ unidadAduana: '' })).success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ cantidadAduana: 0 })).success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ valorUnitarioAduana: 0 })).success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ valorDolares: 0 })).success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse(conLinea({ valorDolares: null })).success,
    ).toBe(false);
  });

  it('servicio (unidad 99 o clave SAT E48): prohíbe fracción, sigue exigiendo valor USD', () => {
    // Unidad 99 sin fracción → válida (CCE159).
    expect(
      EmitirFacturaSchema.safeParse(
        conLinea({ unidadAduana: '99', fraccionArancelaria: null }),
      ).success,
    ).toBe(true);
    // Unidad 99 CON fracción → inválida.
    expect(
      EmitirFacturaSchema.safeParse(
        conLinea({ unidadAduana: '99', fraccionArancelaria: '70071100' }),
      ).success,
    ).toBe(false);
    // Clave de unidad SAT E48 (servicio) también exime la fracción.
    expect(
      EmitirFacturaSchema.safeParse(
        conLinea({ claveUnidadSat: 'E48', unidadAduana: '99', fraccionArancelaria: null }),
      ).success,
    ).toBe(true);
    // Pero el valor USD sigue siendo obligatorio incluso en servicio.
    expect(
      EmitirFacturaSchema.safeParse(
        conLinea({ unidadAduana: '99', fraccionArancelaria: null, valorDolares: 0 }),
      ).success,
    ).toBe(false);
  });

  it('no valida datos de aduana fuera de ExportacionConCce', () => {
    // La misma línea con fracción vacía pasa si el comportamiento no es CCE.
    expect(
      EmitirFacturaSchema.safeParse({
        ...facturaValida,
        lineas: [{ ...lineaValida, fraccionArancelaria: null, valorDolares: 0 }],
      }).success,
    ).toBe(true);
  });
});

describe('EmitirFacturaSchema — matriz SAT método/forma de pago (CFDI40105)', () => {
  it('PPD exige forma de pago 99', () => {
    expect(
      EmitirFacturaSchema.safeParse({
        ...facturaValida,
        metodoPago: 'PPD',
        formaPago: '01',
      }).success,
    ).toBe(false);
    expect(
      EmitirFacturaSchema.safeParse({
        ...facturaValida,
        metodoPago: 'PPD',
        formaPago: '99',
      }).success,
    ).toBe(true);
  });

  it('PUE rechaza forma de pago 99 (Por definir)', () => {
    expect(
      EmitirFacturaSchema.safeParse({
        ...facturaValida,
        metodoPago: 'PUE',
        formaPago: '99',
      }).success,
    ).toBe(false);
    // La forma real del cobro sí pasa.
    expect(EmitirFacturaSchema.safeParse(facturaValida).success).toBe(true);
  });
});
