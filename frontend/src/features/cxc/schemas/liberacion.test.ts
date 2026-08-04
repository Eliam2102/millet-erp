import { describe, expect, it } from 'vitest';
import {
  DecidirLiberacionSchema,
  NuevaAutorizacionSchema,
} from '@/features/cxc/schemas/liberacion';

const GUID = '018f6a5e-0000-7000-8000-000000000001';

describe('DecidirLiberacionSchema', () => {
  const base = {
    pedidoRef: '3000123456',
    clienteId: GUID,
    moneda: 'MXN' as const,
    montoPedido: 15000,
    overrideId: null,
  };

  it('acepta el caso válido sin override', () => {
    expect(DecidirLiberacionSchema.parse(base)).toEqual(base);
  });

  it('acepta override GUID', () => {
    const r = DecidirLiberacionSchema.parse({ ...base, overrideId: GUID });
    expect(r.overrideId).toBe(GUID);
  });

  it('rechaza pedidoRef vacío o > 40 chars (espejo del validator)', () => {
    expect(
      DecidirLiberacionSchema.safeParse({ ...base, pedidoRef: '' }).success,
    ).toBe(false);
    expect(
      DecidirLiberacionSchema.safeParse({ ...base, pedidoRef: 'x'.repeat(41) })
        .success,
    ).toBe(false);
  });

  it('rechaza monto <= 0', () => {
    expect(
      DecidirLiberacionSchema.safeParse({ ...base, montoPedido: 0 }).success,
    ).toBe(false);
  });
});

describe('NuevaAutorizacionSchema', () => {
  const base = {
    beneficiarioUsuarioId: GUID,
    motivo: 'Pago en tránsito confirmado',
    clienteOPedidoRef: '3000123456',
    vigenciaHoras: 24,
  };

  it('acepta el caso válido', () => {
    expect(NuevaAutorizacionSchema.parse(base)).toEqual(base);
  });

  it('rechaza vigencia fuera de 1..24 (espejo del validator)', () => {
    expect(
      NuevaAutorizacionSchema.safeParse({ ...base, vigenciaHoras: 0 }).success,
    ).toBe(false);
    expect(
      NuevaAutorizacionSchema.safeParse({ ...base, vigenciaHoras: 25 }).success,
    ).toBe(false);
  });

  it('rechaza beneficiario no-GUID y motivo vacío', () => {
    expect(
      NuevaAutorizacionSchema.safeParse({ ...base, beneficiarioUsuarioId: 'x' })
        .success,
    ).toBe(false);
    expect(
      NuevaAutorizacionSchema.safeParse({ ...base, motivo: '' }).success,
    ).toBe(false);
  });
});
