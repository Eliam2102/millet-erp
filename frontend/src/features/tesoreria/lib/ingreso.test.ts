import { describe, expect, it } from 'vitest';
import { IngresoSchema, normalizarCuentaOrdenante } from './ingreso';
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
  // 86e3tfdve (opción A): ordenante del cliente con el patrón general del SAT (cuentas ficticias).
  it.each(['0000000010', '00000000011', '000000000000000018', 'CUENTA_FICTICIA_01'])(
    'acepta la cuenta ordenante %s',
    (cuentaOrdenante) => {
      expect(IngresoSchema.safeParse({ ...base, cuentaOrdenante }).success).toBe(true);
    },
  );
  it.each(['000000012', 'CTA.FICTICIA1', 'A'.repeat(51)])('rechaza la cuenta ordenante %s', (cuentaOrdenante) => {
    const r = IngresoSchema.safeParse({ ...base, cuentaOrdenante });
    expect(r.success).toBe(false);
    expect(r.error?.issues[0]?.message).toMatch(/10, 11, 15, 16 o 18 dígitos/);
  });
  it('normaliza la captura quitando espacios y guiones', () => {
    expect(normalizarCuentaOrdenante(' 0000-0000 0000-0016 ')).toBe('0000000000000016');
    expect(normalizarCuentaOrdenante('cuenta_ficticia_01')).toBe('CUENTA_FICTICIA_01');
    expect(normalizarCuentaOrdenante('  ')).toBeUndefined();
  });
});

