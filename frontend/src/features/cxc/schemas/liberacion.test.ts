import { describe, expect, it } from 'vitest';
import {
  AutorizarPendienteSchema,
  CorregirReevaluarSchema,
  DecidirLiberacionSchema,
  NuevaAutorizacionSchema,
} from '@/features/cxc/schemas/liberacion';

const GUID = '018f6a5e-0000-7000-8000-000000000001';

describe('DecidirLiberacionSchema', () => {
  const base = {
    pedidoRef: '7800123456',
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
    clienteOPedidoRef: '7800123456',
    condicion: 1,
    montoAutorizado: 60000,
    moneda: 'MXN',
    vigenciaHoras: 24,
  };

  it('acepta el caso válido', () => {
    expect(NuevaAutorizacionSchema.parse(base)).toEqual(base);
  });

  it('rechaza vigencia menor a 1 h; el máximo lo valida el servidor (M-239)', () => {
    expect(
      NuevaAutorizacionSchema.safeParse({ ...base, vigenciaHoras: 0 }).success,
    ).toBe(false);
    expect(
      NuevaAutorizacionSchema.safeParse({ ...base, vigenciaHoras: 48 }).success,
    ).toBe(true);
  });

  it('M-239: exige condición y un monto mayor a cero', () => {
    expect(NuevaAutorizacionSchema.safeParse({ ...base, condicion: 0 }).success).toBe(false);
    expect(NuevaAutorizacionSchema.safeParse({ ...base, condicion: 4 }).success).toBe(false);
    expect(NuevaAutorizacionSchema.safeParse({ ...base, montoAutorizado: 0 }).success).toBe(false);
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

describe('AutorizarPendienteSchema (C7.3)', () => {
  it('exige motivo (vacío o solo espacios no pasa)', () => {
    expect(AutorizarPendienteSchema.safeParse({ motivo: '' }).success).toBe(false);
    expect(AutorizarPendienteSchema.safeParse({ motivo: '   ' }).success).toBe(false);
  });
  it('acepta motivo y lo recorta; máximo 254', () => {
    expect(AutorizarPendienteSchema.parse({ motivo: '  Pago en tránsito  ' }).motivo).toBe(
      'Pago en tránsito',
    );
    expect(AutorizarPendienteSchema.safeParse({ motivo: 'x'.repeat(255) }).success).toBe(false);
  });
});

describe('CorregirReevaluarSchema (M-238)', () => {
  it('acepta saldo o condición solo con el detalle', () => {
    expect(CorregirReevaluarSchema.safeParse({ tipo: 3, detalle: 'Cobro reaplicado' }).success).toBe(true);
    expect(CorregirReevaluarSchema.safeParse({ tipo: 2, detalle: 'Línea reactivada' }).success).toBe(true);
  });

  it('exige el cliente correcto cuando se corrige el cliente', () => {
    expect(CorregirReevaluarSchema.safeParse({ tipo: 1, detalle: 'Cliente equivocado' }).success).toBe(false);
    expect(
      CorregirReevaluarSchema.safeParse({ tipo: 1, detalle: 'Cliente equivocado', clienteIdCorregido: GUID }).success,
    ).toBe(true);
  });

  it('exige un detalle de al menos 5 caracteres y un tipo válido', () => {
    expect(CorregirReevaluarSchema.safeParse({ tipo: 3, detalle: 'ok' }).success).toBe(false);
    expect(CorregirReevaluarSchema.safeParse({ tipo: 0, detalle: 'Cobro reaplicado' }).success).toBe(false);
  });
});
