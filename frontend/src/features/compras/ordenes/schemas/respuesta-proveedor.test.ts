import { describe, expect, it } from 'vitest';
import {
  RegistrarRespuestaProveedorSchema,
  type RegistrarRespuestaProveedorValues,
} from '@/features/compras/ordenes/schemas/respuesta-proveedor';

const LINEA = '0198a7c2-1111-7000-8000-000000000001';
const base = (o: Partial<RegistrarRespuestaProveedorValues> = {}): RegistrarRespuestaProveedorValues => ({
  archivo: new File(["%PDF-1.4 DEMO ficticio"], "DEMO.pdf", { type: "application/pdf" }),
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
    expect(mensajes(base({ tipo: 2, compromisos: [] }))).toEqual(['Indica el motivo del rechazo.']);
  });
  it('rechazo con fechas prometidas', () => {
    expect(mensajes(base({ tipo: 2, motivo: 'x' }))).toContain('Un rechazo no lleva fechas prometidas.');
  });
  it('confirmación sin compromisos', () => {
    expect(mensajes(base({ compromisos: [] }))).toEqual([]);
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

it('acepta solo el archivo y rechaza sin archivo aunque haya nota de evidencia', () => {
  expect(RegistrarRespuestaProveedorSchema.safeParse({ archivo: base().archivo, compromisos: [] }).success).toBe(true);
  const result = RegistrarRespuestaProveedorSchema.safeParse({ evidencia: 'DEMO ficticio', compromisos: [] });
  expect(result.success).toBe(false);
  if (!result.success) expect(result.error.issues[0]?.message).toContain('Adjunta el archivo');
});
