import { describe, expect, it } from 'vitest';
import { IniciarDevolucionAProveedorSchema } from './devolucion';

const id = '019b1234-5678-7123-8123-123456789abc';
const devolucion = {
  proveedorId: id,
  motivo: 'Material defectuoso',
  recepcionOrigenId: id,
  lineas: [{ articuloId: id, cantidad: 2, unidadMedida: 'PZA', costoUnitarioMxn: 25, lineaRecepcionOrigenId: id }],
};

describe('devolución a proveedor con recepción origen', () => {
  it('acepta una devolución vinculada con identificadores UUID v7', () => {
    expect(IniciarDevolucionAProveedorSchema.safeParse(devolucion).success).toBe(true);
  });

  it.each([null, undefined, ''])('rechaza la recepción ausente: %s', (recepcionOrigenId) => {
    expect(IniciarDevolucionAProveedorSchema.safeParse({ ...devolucion, recepcionOrigenId }).success).toBe(false);
  });

  it.each([null, undefined, ''])('rechaza una línea sin origen: %s', (lineaRecepcionOrigenId) => {
    expect(IniciarDevolucionAProveedorSchema.safeParse({
      ...devolucion, lineas: [{ ...devolucion.lineas[0], lineaRecepcionOrigenId }],
    }).success).toBe(false);
  });
});
