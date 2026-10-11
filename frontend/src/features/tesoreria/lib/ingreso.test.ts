import { describe, expect, it } from 'vitest';
import { IngresoSchema } from './ingreso';
const base = {
  cuentaBancariaId: '00000003-0000-0000-0000-000000000001',
  sucursalId: '00000003-0000-0000-0000-000000000002',
  monto: 100,
  fechaValor: '2026-10-10',
};
describe('Registrar ingreso en una sucursal asignada', () => {
  it('acepta los ids del ERP y prepara la sucursal junto con el ingreso', () => {
    expect(IngresoSchema.parse(base)).toEqual(base);
  });
  it.each([0, -1, Number.NaN])('rechaza importe %s', (monto) => {
    expect(IngresoSchema.safeParse({ ...base, monto }).success).toBe(false);
  });
  it.each(['cuentaBancariaId', 'sucursalId', 'fechaValor'])('no permite enviar sin %s', (campo) => {
    expect(IngresoSchema.safeParse({ ...base, [campo]: '' }).success).toBe(false);
  });
  it('rechaza referencias que exceden el límite del banco', () => {
    expect(IngresoSchema.safeParse({ ...base, referenciaBancaria: 'a'.repeat(121) }).success).toBe(false);
  });
});
