import { describe, expect, it } from 'vitest';
import { PropuestaAplicacionSchema } from '@/features/cxc/schemas/propuesta-aplicacion';

const GUID = '018f6a5e-0000-7000-8000-000000000001';

const base = {
  clienteId: GUID,
  depositoRef: 'SPEI 2026-07-14 #123',
  montoDeposito: 25000,
  moneda: 'MXN' as const,
  remittanceRef: 'REM-2026-07-14',
};

describe('PropuestaAplicacionSchema', () => {
  it('acepta el caso válido', () => {
    expect(PropuestaAplicacionSchema.parse(base)).toEqual(base);
  });

  it('rechaza remittance vacío (regla 2.1: sin remittance no hay propuesta)', () => {
    expect(
      PropuestaAplicacionSchema.safeParse({ ...base, remittanceRef: '' })
        .success,
    ).toBe(false);
  });

  it('rechaza monto <= 0 y depósito sin referencia', () => {
    expect(
      PropuestaAplicacionSchema.safeParse({ ...base, montoDeposito: 0 })
        .success,
    ).toBe(false);
    expect(
      PropuestaAplicacionSchema.safeParse({ ...base, depositoRef: '' }).success,
    ).toBe(false);
  });
});
