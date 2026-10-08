import { describe, expect, it } from 'vitest';
import { DescuentoTipo } from '../api/types';
import { etiquetaDescuento, subtotalLinea } from './subtotal-linea';

describe('Subtotal de OC', () => {
  it('10 × $10 sin descuento son $100', () => {
    expect(subtotalLinea(10, 10)).toBe(100);
    expect(etiquetaDescuento(DescuentoTipo.Monto, 0)).toBe('Sin descuento');
    expect(etiquetaDescuento()).toBe('Descuento no informado');
  });
  it.each([DescuentoTipo.Porcentaje, DescuentoTipo.Monto])('10 × $10 con descuento de 10 (%s) son $90', (tipo) => {
    expect(subtotalLinea(10, 10, tipo, 10)).toBe(90);
  });
  it('distingue porcentaje y monto y limita el monto al bruto', () => {
    expect(etiquetaDescuento(DescuentoTipo.Porcentaje, 10)).toBe('Descuento: 10.00 %');
    expect(etiquetaDescuento(DescuentoTipo.Monto, 10)).toBe('Descuento: $10.00');
    expect(subtotalLinea(10, 10, DescuentoTipo.Monto, 200)).toBe(0);
  });
  it('redondea bruto y descuento porcentual a centavos al par', () => {
    expect(subtotalLinea(1, 1.005)).toBe(1);
    expect(subtotalLinea(1, 1.015)).toBe(1.02);
    expect(subtotalLinea(1, 1, DescuentoTipo.Porcentaje, 0.5)).toBe(1);
    expect(subtotalLinea(1, 1, DescuentoTipo.Porcentaje, 1.5)).toBe(0.98);
  });
});
