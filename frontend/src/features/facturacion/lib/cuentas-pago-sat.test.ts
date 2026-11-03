import { describe, expect, it } from 'vitest';
import {
  admiteBeneficiaria,
  admiteOrdenante,
  enmascararCuenta,
  errorCuentaPago,
} from './cuentas-pago-sat';

/** 86e3tfdve — espejo de CuentasPagoSat (backend). Cuentas ficticias. */
describe('cuentas-pago-sat', () => {
  it('solo las formas bancarizadas admiten cuentas; 06 solo la ordenante', () => {
    expect(admiteOrdenante('03')).toBe(true);
    expect(admiteBeneficiaria('03')).toBe(true);
    expect(admiteOrdenante('01')).toBe(false);
    expect(admiteBeneficiaria('01')).toBe(false);
    expect(admiteOrdenante('06')).toBe(true);
    expect(admiteBeneficiaria('06')).toBe(false);
    expect(admiteOrdenante('99')).toBe(false);
  });

  it.each([
    ['03', 'ordenante', '000000000000000018'],
    ['03', 'ordenante', '0000 0000 0000 0016'],
    ['03', 'beneficiaria', '0000000010'],
    ['02', 'ordenante', '00000000011'],
    ['02', 'beneficiaria', 'ABC_0000000001'],
    ['04', 'ordenante', '0000000000000016'],
    ['29', 'ordenante', '000000000000015'],
  ] as const)('forma %s acepta %s %s', (forma, tipo, cuenta) => {
    expect(errorCuentaPago(forma, tipo, cuenta)).toBeNull();
  });

  it.each([
    ['01', 'ordenante', '0000000010', 'no admite cuenta ordenante'],
    ['03', 'ordenante', '000000000012', '10, 16 o 18 dígitos'],
    ['03', 'beneficiaria', '0000000000000016', '10 o 18 dígitos'],
    ['06', 'beneficiaria', '0000000010', 'no admite cuenta beneficiaria'],
    ['04', 'ordenante', '000000000000000018', '16 dígitos'],
  ] as const)('forma %s rechaza %s %s', (forma, tipo, cuenta, esperado) => {
    const mensaje = errorCuentaPago(forma, tipo, cuenta);
    expect(mensaje).toContain(esperado);
    expect(mensaje).toContain(forma);
    expect(mensaje).not.toContain(cuenta);
  });

  it('cuenta vacía no tiene error', () => {
    expect(errorCuentaPago('01', 'ordenante', '  ')).toBeNull();
    expect(errorCuentaPago('03', 'beneficiaria', null)).toBeNull();
  });

  it('enmascara mostrando solo los últimos cuatro', () => {
    expect(enmascararCuenta('000000000000000018')).toBe('•••• 0018');
    expect(enmascararCuenta('•••• 0018')).toBe('•••• 0018');
    expect(enmascararCuenta(null)).toBeNull();
    expect(enmascararCuenta('')).toBeNull();
  });
});
