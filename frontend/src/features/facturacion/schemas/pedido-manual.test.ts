import { describe, expect, it } from 'vitest';
import {
  PedidoManualSchema,
  PedidoLineaSchema,
} from '@/features/facturacion/schemas/pedido-manual';
import { ComportamientoFiscal } from '@/features/facturacion/api/types';

const lineaValida = {
  productoId: null,
  productoDescripcion: 'Vidrio templado 6mm',
  claveProdServSat: '01010101',
  claveUnidadSat: 'H87',
  cantidad: 2,
  precio: 100,
  descuento: 0,
  requierePedimento: false,
};

const pedidoValido = {
  numeroPedido: null,
  sucursalId: '11111111-1111-4111-8111-111111111111',
  clienteId: '22222222-2222-4222-8222-222222222222',
  clienteNombre: 'Cliente Demo SA de CV',
  // Id del catálogo compartido.canales_venta (FAC-ING-PR3).
  canalVenta: 1,
  comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
  moneda: 'MXN',
  obraId: null,
  obraNombre: null,
  comentarios: null,
  lineas: [lineaValida],
};

describe('PedidoLineaSchema', () => {
  it('acepta una línea válida', () => {
    expect(PedidoLineaSchema.safeParse(lineaValida).success).toBe(true);
  });

  it('rechaza cantidad <= 0', () => {
    const r = PedidoLineaSchema.safeParse({ ...lineaValida, cantidad: 0 });
    expect(r.success).toBe(false);
  });

  it('rechaza descripción vacía', () => {
    const r = PedidoLineaSchema.safeParse({
      ...lineaValida,
      productoDescripcion: '',
    });
    expect(r.success).toBe(false);
  });

  it('rechaza precio negativo', () => {
    const r = PedidoLineaSchema.safeParse({ ...lineaValida, precio: -1 });
    expect(r.success).toBe(false);
  });
});

describe('PedidoManualSchema', () => {
  it('acepta un pedido válido', () => {
    expect(PedidoManualSchema.safeParse(pedidoValido).success).toBe(true);
  });

  it('exige al menos una línea', () => {
    const r = PedidoManualSchema.safeParse({ ...pedidoValido, lineas: [] });
    expect(r.success).toBe(false);
  });

  it('exige clienteId con formato GUID', () => {
    const r = PedidoManualSchema.safeParse({
      ...pedidoValido,
      clienteId: 'no-es-guid',
    });
    expect(r.success).toBe(false);
  });

  it('exige moneda de 3 caracteres', () => {
    const r = PedidoManualSchema.safeParse({ ...pedidoValido, moneda: 'PESOS' });
    expect(r.success).toBe(false);
  });

  it('rechaza canal de venta no positivo o no entero (id de catálogo)', () => {
    // FAC-ING-PR3: el canal es un id del catálogo administrable — la
    // existencia/actividad la valida el backend; el schema solo exige
    // un entero positivo.
    expect(
      PedidoManualSchema.safeParse({ ...pedidoValido, canalVenta: 0 }).success,
    ).toBe(false);
    expect(
      PedidoManualSchema.safeParse({ ...pedidoValido, canalVenta: -1 }).success,
    ).toBe(false);
    expect(
      PedidoManualSchema.safeParse({ ...pedidoValido, canalVenta: 1.5 }).success,
    ).toBe(false);
  });

  it('acepta cualquier id positivo de canal (la BD valida existencia)', () => {
    const r = PedidoManualSchema.safeParse({ ...pedidoValido, canalVenta: 999 });
    expect(r.success).toBe(true);
  });
});
