import { describe, expect, it } from 'vitest';
import {
  RegistrarRespuestaProveedorSchema,
  type RegistrarRespuestaProveedorValues,
} from '@/features/compras/ordenes/schemas/respuesta-proveedor';

const LINEA = '0198a7c2-1111-7000-8000-000000000001';
const base = (o: Partial<RegistrarRespuestaProveedorValues> = {}): RegistrarRespuestaProveedorValues => ({
  tipo: 1,
  fechaRespuesta: '2026-10-17',
  evidencia: 'Correo P-881',
  compromisos: [{ lineaOrdenCompraId: LINEA, cantidad: 10, fechaPrometida: '2026-10-20' }],
  ...o,
});
const mensajes = (v: RegistrarRespuestaProveedorValues) => {
  const r = RegistrarRespuestaProveedorSchema.safeParse(v);
  return r.success ? [] : r.error.issues.map((i) => i.message);
};

describe('RegistrarRespuestaProveedorSchema', () => {
  it('acepta una confirmación completa', () => {
    expect(RegistrarRespuestaProveedorSchema.safeParse(base()).success).toBe(true);
  });
  it('acepta un rechazo con motivo', () => {
    expect(
      RegistrarRespuestaProveedorSchema.safeParse(base({ tipo: 2, motivo: 'Sin stock', compromisos: [] })).success,
    ).toBe(true);
  });
  it('rechazo sin motivo', () => {
    expect(mensajes(base({ tipo: 2, compromisos: [] }))).toContain('Indica el motivo del rechazo del proveedor.');
  });
  it('rechazo con fechas prometidas', () => {
    expect(mensajes(base({ tipo: 2, motivo: 'x' }))).toContain('Un rechazo no lleva fechas prometidas.');
  });
  it('confirmación sin compromisos', () => {
    expect(mensajes(base({ compromisos: [] }))).toContain('Captura al menos una fecha prometida.');
  });
  it('cantidad cero', () => {
    expect(
      mensajes(base({ compromisos: [{ lineaOrdenCompraId: LINEA, cantidad: 0, fechaPrometida: '2026-10-20' }] })),
    ).toContain('La cantidad prometida debe ser mayor a cero.');
  });
  it('fecha prometida anterior a la respuesta', () => {
    expect(
      mensajes(base({ compromisos: [{ lineaOrdenCompraId: LINEA, cantidad: 1, fechaPrometida: '2026-10-16' }] })),
    ).toContain('La fecha prometida no puede ser anterior a la fecha de respuesta.');
  });
});
